using System;
using System.Globalization;
using Microsoft.Xrm.Sdk;

namespace Ab4.Plugins
{
    // Step: account Update (filtering attribute: creditlimit), PostOperation (40), Synchronous
    // Pre Image  alias "PreImage"  columns: name, creditlimit
    // Post Image alias "PostImage" columns: creditlimit
    // Unsecure configuration: "fail" forces an exception after the note is written (rollback demo)
    public sealed class CreditLimitChangeAudit : IPlugin
    {
        private const string PreImageAlias = "PreImage";
        private const string PostImageAlias = "PostImage";
        private readonly bool _failAfterWrite;

        public CreditLimitChangeAudit(string unsecureConfig, string secureConfig)
        {
            _failAfterWrite = string.Equals((unsecureConfig ?? string.Empty).Trim(), "fail", StringComparison.OrdinalIgnoreCase);
        }

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            if (context.Stage != 40 || context.MessageName != "Update")
                throw new InvalidPluginExecutionException("CreditLimitChangeAudit must be registered on Update, PostOperation (40).");

            // Only react to direct user/API updates, not updates caused by other plug-ins.
            if (context.Depth > 1)
            {
                tracing.Trace("CreditLimitChangeAudit: depth {0}, skipping.", context.Depth);
                return;
            }

            Entity pre, post;
            if (!context.PreEntityImages.TryGetValue(PreImageAlias, out pre)
                || !context.PostEntityImages.TryGetValue(PostImageAlias, out post))
            {
                throw new InvalidPluginExecutionException(
                    "CreditLimitChangeAudit: register Pre Image 'PreImage' (name, creditlimit) and Post Image 'PostImage' (creditlimit) on this step.");
            }

            var before = pre.GetAttributeValue<Money>("creditlimit");
            var after = post.GetAttributeValue<Money>("creditlimit");

            // Filtering attributes fire when creditlimit is in the request, even if the value didn't change.
            if (Amount(before) == Amount(after))
            {
                tracing.Trace("CreditLimitChangeAudit: creditlimit submitted but unchanged ({0}).", Fmt(after));
                return;
            }

            try
            {
                var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
                var service = factory.CreateOrganizationService(context.UserId);

                var note = new Entity("annotation");
                note["objectid"] = new EntityReference("account", context.PrimaryEntityId);
                note["subject"] = "Credit limit changed";
                note["notetext"] = string.Format(CultureInfo.InvariantCulture,
                    "{0}: credit limit {1} -> {2}. UserId {3}, InitiatingUserId {4}, CorrelationId {5}.",
                    pre.GetAttributeValue<string>("name"), Fmt(before), Fmt(after),
                    context.UserId, context.InitiatingUserId, context.CorrelationId);

                var noteId = service.Create(note);
                tracing.Trace("CreditLimitChangeAudit: note {0} created ({1} -> {2}).", noteId, Fmt(before), Fmt(after));

                if (_failAfterWrite)
                {
                    throw new InvalidPluginExecutionException(
                        "CreditLimitChangeAudit forced failure after writing note " + noteId
                        + ". The note and the credit limit change both roll back.");
                }
            }
            catch (InvalidPluginExecutionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                tracing.Trace("CreditLimitChangeAudit: {0}", ex.ToString());
                throw new InvalidPluginExecutionException("CreditLimitChangeAudit failed: " + ex.Message, ex);
            }
        }

        private static decimal? Amount(Money m)
        {
            return m == null ? (decimal?)null : m.Value;
        }

        private static string Fmt(Money m)
        {
            return m == null ? "(blank)" : m.Value.ToString("N2", CultureInfo.InvariantCulture);
        }
    }
}
