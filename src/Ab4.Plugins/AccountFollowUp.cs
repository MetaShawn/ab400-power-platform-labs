using System;
using System.Linq;
using Microsoft.Xrm.Sdk;

namespace Ab4.Plugins
{
    // Step: account Create, PostOperation (40), Asynchronous
    public sealed class AccountFollowUp : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            if (context.Stage != 40 || context.MessageName != "Create" || context.Mode != 1)
                throw new InvalidPluginExecutionException("AccountFollowUp must be registered on Create, PostOperation (40), Asynchronous.");

            tracing.Trace("Context: Message={0} Entity={1} Stage={2} Mode={3} Depth={4} IsInTransaction={5}",
                context.MessageName, context.PrimaryEntityName, context.Stage, context.Mode, context.Depth, context.IsInTransaction);
            tracing.Trace("Users: UserId={0} InitiatingUserId={1} CorrelationId={2} OperationId={3}",
                context.UserId, context.InitiatingUserId, context.CorrelationId, context.OperationId);
            tracing.Trace("InputParameters: {0}", string.Join(", ", context.InputParameters.Select(p => p.Key)));
            tracing.Trace("OutputParameters: {0}", string.Join(", ", context.OutputParameters.Select(p => p.Key)));

            object idObj;
            if (!context.OutputParameters.TryGetValue("id", out idObj) || !(idObj is Guid))
                throw new InvalidPluginExecutionException("AccountFollowUp: OutputParameters has no 'id'. Is the step on PostOperation?");
            var accountId = (Guid)idObj;

            var target = (Entity)context.InputParameters["Target"];
            var name = target.GetAttributeValue<string>("name") ?? "(no name)";

            try
            {
                var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
                var service = factory.CreateOrganizationService(context.UserId);

                var task = new Entity("task");
                task["subject"] = "Follow up: " + name;
                task["description"] = "Created by AB400 AccountFollowUp (async PostOperation). Account number in Target: "
                    + (target.GetAttributeValue<string>("accountnumber") ?? "(none)");
                task["scheduledend"] = DateTime.UtcNow.AddDays(7);
                task["regardingobjectid"] = new EntityReference("account", accountId);

                var taskId = service.Create(task);
                tracing.Trace("AccountFollowUp: task {0} created for account {1}.", taskId, accountId);
            }
            catch (Exception ex)
            {
                tracing.Trace("AccountFollowUp: {0}", ex.ToString());
                throw new InvalidPluginExecutionException("AccountFollowUp failed: " + ex.Message, ex);
            }
        }
    }
}
