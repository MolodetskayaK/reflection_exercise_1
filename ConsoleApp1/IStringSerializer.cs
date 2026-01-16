using System;

public interface IStringSerializer
{
    string Serialize(object obj);
    object Deserialize(string data, Type type);
    T Deserialize<T>(string data) where T : new();
}
