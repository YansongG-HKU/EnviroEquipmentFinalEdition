namespace SiemensS7Demo.Domain.Mqtt;

/// <summary>
/// MQTT Quality-of-Service levels (mirrors <c>MQTTnet.Protocol.MqttQualityOfServiceLevel</c>
/// so we can keep the domain layer free of the MQTTnet dependency).
/// </summary>
public enum MqttQos
{
    AtMostOnce = 0,
    AtLeastOnce = 1,
    ExactlyOnce = 2
}
