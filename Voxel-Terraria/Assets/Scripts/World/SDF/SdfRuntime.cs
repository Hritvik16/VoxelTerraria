using Unity.Collections;

public static class SdfRuntime
{
    public static SdfContext Context;
    public static bool Initialized { get; private set; } = false;

    public static void SetContext(SdfContext ctx)
    {
        // If old context exists, dispose it
        if (Initialized)
        {
            Context.Dispose();
        }

        Context = ctx;
        Initialized = true;
    }

    public static void Dispose()
    {
        if (Initialized)
        {
            Context.Dispose();
            Initialized = false;
        }
    }
}
