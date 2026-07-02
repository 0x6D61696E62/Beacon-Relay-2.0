using BeaconRelay.LpdReceiver.Data;

namespace BeaconRelay.LpdReceiver.Services;

public sealed class DeliveryWorkItemEnqueuer(AppDbContext dbContext)
{
    public void EnqueueForReceivedFile(ReceivedFileRecord receivedFile, IReadOnlyList<ProcessingRuleRecord> matchedRules, DateTime enqueueUtc)
    {
        foreach (var rule in matchedRules)
        {
            foreach (var folder in rule.FolderDestinations.Where(x => x.IsEnabled).OrderBy(x => x.DestinationOrder))
            {
                dbContext.DeliveryWorkItems.Add(new DeliveryWorkItemRecord
                {
                    ReceivedFileId = receivedFile.Id,
                    RuleId = rule.Id,
                    DestinationType = DeliveryDestinationType.Folder,
                    DestinationId = folder.Id,
                    Status = DeliveryWorkItemStatus.Pending,
                    Priority = rule.Priority,
                    AttemptCount = 0,
                    NextAttemptUtc = enqueueUtc,
                    CreatedUtc = enqueueUtc,
                    UpdatedUtc = enqueueUtc,
                });
            }

            foreach (var forward in rule.ForwardDestinations.Where(x => x.IsEnabled).OrderBy(x => x.DestinationOrder))
            {
                dbContext.DeliveryWorkItems.Add(new DeliveryWorkItemRecord
                {
                    ReceivedFileId = receivedFile.Id,
                    RuleId = rule.Id,
                    DestinationType = DeliveryDestinationType.ForwardLpd,
                    DestinationId = forward.Id,
                    Status = DeliveryWorkItemStatus.Pending,
                    Priority = rule.Priority,
                    AttemptCount = 0,
                    NextAttemptUtc = enqueueUtc,
                    CreatedUtc = enqueueUtc,
                    UpdatedUtc = enqueueUtc,
                });
            }
        }
    }
}
