namespace RGMS.Lib.Service;

public sealed record DaqDeviceInfo(
    string Name,
    string ProductType,
    string SerialNumber,
    IReadOnlyList<string> AnalogInputChannels,
    IReadOnlyList<string> AnalogOutputChannels);
