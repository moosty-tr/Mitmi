namespace Mitmi.Protocols.Modbus.Diagnostics;

public interface IModbusObservedValueUpdateSink
{
    ValueTask EmitAsync(
        ModbusObservedValueUpdateGroup updateGroup,
        CancellationToken cancellationToken);
}
