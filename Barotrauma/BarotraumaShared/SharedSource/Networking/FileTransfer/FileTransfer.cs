namespace Barotrauma.Networking
{
    public enum FileTransferStatus
    {
        NotStarted, Sending, Receiving, Finished, Canceled, Error
    }

    public enum FileTransferMessageType
    {
        Unknown, Initiate, Data, TransferOnSameMachine, Cancel
    }

    public enum FileTransferType
    {
        Submarine, CampaignSave
    }
}
