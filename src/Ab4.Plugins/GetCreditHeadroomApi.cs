using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Ab4.Plugins
{
    // Main operation (stage 30) for custom API ab4_GetCreditHeadroom.
    // Function (HTTP GET), Binding: Global, Custom Processing Step Type: None.
    // Request:  AccountId (Guid, required)
    // Response: CurrentLimit (Decimal), AutoApproveCeiling (Decimal), Headroom (Decimal)
    // A function must not change data and must return at least one response property.
    public sealed class GetCreditHeadroomApi : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            if (context.MessageName != CreditPolicy.HeadroomMessage || context.Stage != 30)
                throw new InvalidPluginExecutionException(
                    "GetCreditHeadroomApi must be the main operation (stage 30) of " + CreditPolicy.HeadroomMessage + ".");

            var accountId = (Guid)context.InputParameters["AccountId"];

            try
            {
                var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
                var service = factory.CreateOrganizationService(context.UserId);

                var account = service.Retrieve("account", accountId, new ColumnSet("creditlimit"));
                var money = account.GetAttributeValue<Money>("creditlimit");
                var current = money == null ? 0m : money.Value;
                var headroom = Math.Max(0m, CreditPolicy.AutoApproveCeiling - current);

                context.OutputParameters["CurrentLimit"] = current;
                context.OutputParameters["AutoApproveCeiling"] = CreditPolicy.AutoApproveCeiling;
                context.OutputParameters["Headroom"] = headroom;

                tracing.Trace("GetCreditHeadroomApi: account {0} current {1} headroom {2} IsInTransaction={3}",
                    accountId, current, headroom, context.IsInTransaction);
            }
            catch (Exception ex)
            {
                tracing.Trace("GetCreditHeadroomApi: {0}", ex.ToString());
                throw new InvalidPluginExecutionException("GetCreditHeadroomApi failed: " + ex.Message, ex);
            }
        }
    }
}