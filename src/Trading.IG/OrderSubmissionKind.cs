namespace Trading.IG;

public enum OrderSubmissionKind
{
    Open = 1,
    Close = 2,
    WorkingOrderCreate = 3,
    WorkingOrderUpdate = 4,
    WorkingOrderCancel = 5,
    PositionUpdate = 6,
}
