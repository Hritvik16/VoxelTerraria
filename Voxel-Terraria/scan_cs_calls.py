import os
import re
from collections import defaultdict

# ---------------------------------------------------------------------------
# CONFIG
# ---------------------------------------------------------------------------

# Root folder for your game code
ROOT_DIR = os.path.join("Assets", "Scripts")

# Unity "magic" methods that are used via reflection / naming conventions.
# We DO NOT want to treat these as unused even if there are no explicit calls.
UNITY_MAGIC_METHODS = {
    "Awake",
    "Start",
    "Update",
    "FixedUpdate",
    "LateUpdate",
    "OnEnable",
    "OnDisable",
    "OnDestroy",
    "Reset",
    "OnGUI",
    "OnValidate",
    "OnDrawGizmos",
    "OnDrawGizmosSelected",
    "OnApplicationQuit",
    "OnApplicationPause",
    "OnTriggerEnter",
    "OnTriggerStay",
    "OnTriggerExit",
    "OnTriggerEnter2D",
    "OnTriggerStay2D",
    "OnTriggerExit2D",
    "OnCollisionEnter",
    "OnCollisionStay",
    "OnCollisionExit",
    "OnCollisionEnter2D",
    "OnCollisionStay2D",
    "OnCollisionExit2D",
}

# Method names we know are bogus from regex (like float3, etc.)
BAD_METHOD_NAMES = {
    "float", "float2", "float3", "float4",
    "int", "int2", "int3", "int4",
}


# ---------------------------------------------------------------------------
# REGEXES
# ---------------------------------------------------------------------------

# Very simple class declaration regex: "public class Foo", "class Foo", etc.
CLASS_REGEX = re.compile(r'\bclass\s+([A-Za-z_]\w*)')

# Very simple method signature regex.
# This will miss some edge cases, but is OK for typical Unity project code.
METHOD_REGEX = re.compile(
    r'^\s*'
    r'(?:public|private|protected|internal|static|sealed|partial|virtual|override|async|\s)+\s+'
    r'([\w<>,\[\]?]+)\s+'      # return type
    r'([A-Za-z_]\w*)\s*\('     # method name
)

# Template for searching calls: "MethodName("
CALL_TEMPLATE = r'\b{method}\s*\('


# ---------------------------------------------------------------------------
# DATA STRUCTURES
# ---------------------------------------------------------------------------

class MethodDef:
    def __init__(self, class_name, method_name, file_path, line_no):
        self.class_name = class_name
        self.method_name = method_name
        self.file_path = file_path
        self.line_no = line_no

    @property
    def rel_file(self):
        rel = os.path.relpath(self.file_path, ROOT_DIR)
        return rel.replace(os.sep, '/')

    @property
    def id(self):
        # ID used as a key: ClassName.MethodName at a specific file+line.
        return f"{self.class_name}.{self.method_name}@{self.rel_file}:{self.line_no}"

    @property
    def display_name(self):
        # World/SDF/Foo.cs -> World/SDF/Foo
        rel = self.rel_file
        base_no_ext = os.path.splitext(rel)[0]
        return f"{base_no_ext}:{self.class_name}.{self.method_name}"


# ---------------------------------------------------------------------------
# SCANNING
# ---------------------------------------------------------------------------

def find_cs_files(root):
    for dirpath, _, filenames in os.walk(root):
        for fn in filenames:
            if fn.endswith(".cs"):
                yield os.path.join(dirpath, fn)


def collect_methods():
    """
    Return:
      methods: list[MethodDef]
      file_lines: dict[path -> list[str]]  (cached file contents)
    """
    methods = []
    file_lines = {}

    for path in find_cs_files(ROOT_DIR):
        with open(path, "r", encoding="utf-8", errors="ignore") as f:
            lines = f.readlines()
        file_lines[path] = lines

        current_class = None
        for i, line in enumerate(lines, start=1):
            # Track current class
            class_match = CLASS_REGEX.search(line)
            if class_match:
                current_class = class_match.group(1)

            m = METHOD_REGEX.match(line)
            if not m:
                continue

            ret_type = m.group(1)
            method_name = m.group(2)

            # Filter bogus "methods" like float3, int3, etc.
            if method_name in BAD_METHOD_NAMES:
                continue

            # If there's somehow no class in scope, call it "Global"
            class_name = current_class or "Global"

            methods.append(MethodDef(class_name, method_name, path, i))

    return methods, file_lines


def find_calls(methods, file_lines):
    """
    Build a map method_id -> set of (file_path, line_no) where it's called.
    IMPORTANT: excludes the definition line itself.
    """
    calls = defaultdict(set)

    # Group methods by simple name for faster lookup
    methods_by_name = defaultdict(list)
    for m in methods:
        methods_by_name[m.method_name].append(m)

    for file_path, lines in file_lines.items():
        for i, line in enumerate(lines, start=1):
            # Strip off inline // comments so commented-out calls don't count
            code_part = line.split("//", 1)[0]
            stripped = code_part.strip()

            if not stripped:
                continue
            if stripped.startswith("using "):
                continue

            for method_name, defs in methods_by_name.items():
                if method_name not in code_part:
                    continue

                call_regex = re.compile(CALL_TEMPLATE.format(method=re.escape(method_name)))
                if not call_regex.search(code_part):
                    continue

                # We have "methodName(" in this line. For each definition with this name:
                for md in defs:
                    # Skip counting the method's own definition line as a call.
                    if file_path == md.file_path and i == md.line_no:
                        continue

                    calls[md.id].add((file_path, i))

    return calls


# ---------------------------------------------------------------------------
# MAIN
# ---------------------------------------------------------------------------

def main():
    if not os.path.isdir(ROOT_DIR):
        print(f"ERROR: {ROOT_DIR} does not exist. Adjust ROOT_DIR in the script.")
        return

    print(f"Scanning C# files under {ROOT_DIR} ...")
    methods, file_lines = collect_methods()
    print(f"Discovered {len(methods)} methods. Searching for call sites...\n")

    calls = find_calls(methods, file_lines)

    # Filter to methods that have *no* callsites and are not Unity magic methods
    unused = []
    for m in methods:
        # Skip Unity magic methods by name
        if m.method_name in UNITY_MAGIC_METHODS:
            continue

        # Skip constructors (method name == class name)
        if m.method_name == m.class_name:
            continue

        method_callsites = calls.get(m.id, set())
        if not method_callsites:
            unused.append(m)

    # Print results
    print("=" * 80)
    print("POTENTIALLY UNUSED METHODS (no explicit call sites found):")
    print("=" * 80)

    if not unused:
        print("None found (or everything is called via code or Unity/attributes).")
        return

    for m in unused:
        print(f"- {m.display_name}")
        print(f"    defined in: {m.file_path}:{m.line_no}")
    print("\nNOTE:")
    print("  • This is static text search — treat results as *candidates*.")
    print("  • Methods used only via UnityEvents, attributes, or reflection")
    print("    will appear here even though they are actually used.")


if __name__ == "__main__":
    main()
