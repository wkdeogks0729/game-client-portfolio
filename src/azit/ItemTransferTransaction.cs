using System;

public sealed class ItemTransferTransaction
{
    private readonly Func<bool> removeSource;
    private readonly Func<bool> restoreSource;
    private bool sourceRemoved;
    private bool completed;

    public ItemInstance Item { get; }
    public bool IsCompleted => completed;

    public ItemTransferTransaction(
        ItemInstance item,
        Func<bool> removeSource,
        Func<bool> restoreSource,
        bool sourceAlreadyRemoved = false)
    {
        Item = item;
        this.removeSource = removeSource;
        this.restoreSource = restoreSource;
        sourceRemoved = sourceAlreadyRemoved;
    }

    public bool RemoveSource()
    {
        if (completed || Item == null)
            return false;

        if (sourceRemoved)
            return true;

        sourceRemoved = removeSource != null && removeSource();
        return sourceRemoved;
    }

    public bool TryMove(Func<bool> placeDestination)
    {
        if (completed || placeDestination == null || !RemoveSource())
            return false;

        if (placeDestination())
        {
            completed = true;
            return true;
        }

        Rollback();
        return false;
    }

    public ItemStackTransferResult TryMerge(ItemInstance target, out int movedAmount)
    {
        return TryMergeInternal(null, target, out movedAmount);
    }

    public ItemStackTransferResult TryMerge(
        IItemGridContainer destination,
        ItemInstance target,
        out int movedAmount)
    {
        return TryMergeInternal(destination, target, out movedAmount);
    }

    private ItemStackTransferResult TryMergeInternal(
        IItemGridContainer destination,
        ItemInstance target,
        out int movedAmount)
    {
        movedAmount = 0;
        if (completed || Item == null || target == null)
            return ItemStackTransferResult.None;

        ItemStackTransferResult result = destination != null
            ? ItemTransferService.TryMergeStacks(destination, Item, target, out movedAmount)
            : ItemTransferService.TryMergeStacks(Item, target, out movedAmount);
        if (result == ItemStackTransferResult.None)
            return result;

        if (result == ItemStackTransferResult.FullyMerged && !RemoveSource())
            throw new InvalidOperationException("Merged source item could not be removed from its source container.");

        if (result == ItemStackTransferResult.PartiallyMerged && sourceRemoved)
        {
            if (restoreSource == null || !restoreSource())
                throw new InvalidOperationException("Partially merged item could not be restored to its source container.");

            sourceRemoved = false;
        }

        completed = true;
        return result;
    }

    public bool TryTake(out ItemInstance item)
    {
        item = null;
        if (!RemoveSource())
            return false;

        item = Item;
        completed = true;
        return true;
    }

    public void CompleteWithoutMove()
    {
        if (!completed)
            completed = true;
    }

    public bool Rollback()
    {
        if (completed)
            return true;

        if (!sourceRemoved)
        {
            completed = true;
            return true;
        }

        bool restored = restoreSource != null && restoreSource();
        if (restored)
        {
            sourceRemoved = false;
            completed = true;
        }

        return restored;
    }
}
