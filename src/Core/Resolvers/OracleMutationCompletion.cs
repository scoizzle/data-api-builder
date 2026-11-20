// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Commit and rollback policy for one Oracle mutation. The original work exception is
    /// always what propagates. A rollback failure is logged and discarded. Commit is not
    /// attempted after a failed unit of work, and a failed commit is not followed by rollback.
    /// </summary>
    internal static class OracleMutationCompletion
    {
        public static async Task CompleteAsync(
            Func<Task> work,
            Action rollback,
            Action commit,
            Action<Exception, Exception>? logRollbackFailure = null)
        {
            try
            {
                await work();
            }
            catch (Exception workException)
            {
                try
                {
                    rollback();
                }
                catch (Exception rollbackException)
                {
                    logRollbackFailure?.Invoke(rollbackException, workException);
                }

                throw;
            }

            commit();
        }
    }
}
