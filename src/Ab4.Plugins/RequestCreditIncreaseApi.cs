using System;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Ab4.Plugins
{
    // Main operation (stage 30) for custom API ab4_RequestCreditIncrease.
    // Binding: Entity (account), so the platform adds the Target (EntityReference) parameter.
    // Request:  RequestedLimit (Decimal, required), Justification (String, optional)
    // Response: Approved (Boolean), NewCreditLimit (Decimal), Outcome (String)
    // Registered by setting the custom API's Plugin Type. No step is registered for it.
    public sealed class RequestCreditIncreaseApi : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            if (context.MessageName != CreditPolicy.RequestMessage || context.Stage != 30)
                throw new InvalidPluginExecutionException(
                    "RequestCreditIncreaseApi must be the main operation (stage 30) of " + CreditPolicy.RequestMessage + ".");

            var target = context.InputParameters["Target"] as EntityReference;
            if (target == null || target.LogicalName != "account")
                throw new InvalidPluginExecutionException("Target must be an account.");

            var requested = (decimal)context.InputParameters["RequestedLimit"];
            if (requested <= 0m)
                throw new InvalidPluginExecutionException("RequestedLimit must be greater than zero.");

            // Optional parameters may be absent from InputParameters; never index them directly.
            string justification = null;
            object raw;
            if (context.InputParameters.TryGetValue("Justification", out raw))
                justification = raw as string;

            try
            {
                var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
                var service = factory.CreateOrganizationService(context.UserId);

                // No images on a main operation, so read the current state explicitly and narrowly.
                var account = service.Retrieve("account", target.Id, new ColumnSet("name", "creditlimit"));
                var currentMoney = account.GetAttributeValue<Money>("creditlimit");
                var current = currentMoney == null ? 0m : currentMoney.Value;
                var name = account.GetAttributeValue<string>("name") ?? "(no name)";

                tracing.Trace("RequestCreditIncreaseApi: account {0} '{1}', current {2}, requested {3}, ceiling {4}, Depth={5}, IsInTransaction={6}",
                    target.Id, name, Fmt(current), Fmt(requested), Fmt(CreditPolicy.AutoApproveCeiling),
                    context.Depth, context.IsInTransaction);

                if (requested <= current)
                    throw new InvalidPluginExecutionException(string.Format(CultureInfo.InvariantCulture,
                        "Requested limit {0:N2} must be higher than the current limit {1:N2}.", requested, current));

                if (requested <= CreditPolicy.AutoApproveCeiling)
                {
                    var update = new Entity("account", target.Id);
                    update["creditlimit"] = new Money(requested);
                    // Runs the W01 Update steps one level deeper (Depth + 1), inside this transaction.
                    service.Update(update);

                    context.OutputParameters["Approved"] = true;
                    context.OutputParameters["NewCreditLimit"] = requested;
                    context.OutputParameters["Outcome"] = "Auto-approved (at or below " + Fmt(CreditPolicy.AutoApproveCeiling) + ").";
                    tracing.Trace("RequestCreditIncreaseApi: auto-approved, creditlimit set to {0}.", Fmt(requested));
                    return;
                }

                // Over the ceiling: change nothing, emit the business event, let subscribers route it.
                var evt = new OrganizationRequest(CreditPolicy.EventMessage);
                evt["AccountId"] = target.Id;
                evt["AccountName"] = name;
                evt["CurrentLimit"] = current;
                evt["RequestedLimit"] = requested;
                if (!string.IsNullOrWhiteSpace(justification))
                    evt["Justification"] = justification;
                service.Execute(evt);

                context.OutputParameters["Approved"] = false;
                context.OutputParameters["NewCreditLimit"] = current;
                context.OutputParameters["Outcome"] = "Above " + Fmt(CreditPolicy.AutoApproveCeiling) + ". Routed for finance approval.";
                tracing.Trace("RequestCreditIncreaseApi: emitted {0} for account {1}.", CreditPolicy.EventMessage, target.Id);
            }
            catch (InvalidPluginExecutionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                tracing.Trace("RequestCreditIncreaseApi: {0}", ex.ToString());
                throw new InvalidPluginExecutionException("RequestCreditIncreaseApi failed: " + ex.Message, ex);
            }
        }

        private static string Fmt(decimal d)
        {
            return d.ToString("N2", CultureInfo.InvariantCulture);
        }
    }
}