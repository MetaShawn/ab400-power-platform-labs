using System;
using System.Globalization;
using Microsoft.Xrm.Sdk;

namespace Ab4.Plugins
{
    // Steps: account Create, and account Update (filtering attribute: creditlimit)
    // Stage: PreValidation (10), Synchronous
    // Unsecure configuration: max credit limit, invariant culture, e.g. 150000
    public sealed class CreditLimitGuard : IPlugin
    {
        private const decimal DefaultMax = 250000m;

        // Set once per step registration and never mutated, so instance caching is safe.
        private readonly decimal _max;

        public CreditLimitGuard(string unsecureConfig, string secureConfig)
        {
            decimal parsed;
            _max = decimal.TryParse(unsecureConfig, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : DefaultMax;
        }

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            if (context.Stage != 10)
                throw new InvalidPluginExecutionException("CreditLimitGuard must be registered on PreValidation (10).");

            object raw;
            if (!context.InputParameters.TryGetValue("Target", out raw)) return;
            var target = raw as Entity;
            if (target == null || target.LogicalName != "account") return;

            if (!target.Contains("creditlimit"))
            {
                tracing.Trace("CreditLimitGuard: {0} without creditlimit in Target, nothing to check.", context.MessageName);
                return;
            }

            var limit = target.GetAttributeValue<Money>("creditlimit");
            if (limit == null) return; // clearing the limit is allowed

            tracing.Trace("CreditLimitGuard: {0} depth {1}, requested {2}, ceiling {3}, IsInTransaction={4}",
                context.MessageName, context.Depth,
                limit.Value.ToString("N2", CultureInfo.InvariantCulture),
                _max.ToString("N2", CultureInfo.InvariantCulture),
                context.IsInTransaction);

            if (limit.Value > _max)
            {
                throw new InvalidPluginExecutionException(string.Format(CultureInfo.InvariantCulture,
                    "Credit limit {0:N2} exceeds the approval ceiling of {1:N2}. Get finance approval first.",
                    limit.Value, _max));
            }
        }
    }
}
