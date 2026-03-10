namespace Trading.Abstractions;

public readonly record struct InstrumentId
{
    public InstrumentId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Instrument identifier is required.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
