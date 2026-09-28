using System;
using System.Globalization;
using Microsoft.Xrm.Sdk;

namespace Ab4.Plugins
{
    // Step: account Create, PreOperation (20), Synchronous
    public sealed class AccountNumberDefaulter : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            if (context.Stage != 20 || context.MessageName != "Create")
                throw new InvalidPluginExecutionException("AccountNumberDefaulter must be registered on Create, PreOperation (20).");

            object raw;
            if (!context.InputParameters.TryGetValue("Target", out raw)) return;
            var target = raw as Entity;
            if (target == null) return;

            var existing = target.GetAttributeValue<string>("accountnumber");
            if (!string.IsNullOrWhiteSpace(existing))
            {
                tracing.Trace("AccountNumberDefaulter: caller supplied {0}, leaving it.", existing);
                return;
            }

            // accountnumber is 20 chars max; this is 17.
            var number = "AB4-"
                + DateTime.UtcNow.ToString("yyMMdd", CultureInfo.InvariantCulture)
                + "-"
                + Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();

            // No service.Update call: changing Target here is free and inside the transaction.
            target["accountnumber"] = number;

            tracing.Trace("AccountNumberDefaulter: set accountnumber {0}. IsInTransaction={1}", number, context.IsInTransaction);
        }
    }
}
