using System;
using System.Threading;

namespace Ant.GrasshopperPlugIn.Helpers
{
    internal static class NativeMeshGate
    {
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        // Gmsh itself remains synchronous. Cancel queued Ant requests without entering Gmsh;
        // once started, wait for the native call to return before acknowledging cancellation.
        internal static T Execute<T>(CancellationToken token, Func<T> generate)
        {
            Gate.Wait(token);
            try
            {
                token.ThrowIfCancellationRequested();
                var result = generate();
                token.ThrowIfCancellationRequested();
                return result;
            }
            finally { Gate.Release(); }
        }
    }
}
