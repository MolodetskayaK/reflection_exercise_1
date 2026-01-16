using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Xml;

public sealed class NewtonsoftJsonSerializer : IStringSerializer
{
    private readonly JsonSerializerSettings _settings = new()
    {
        Formatting = Newtonsoft.Json.Formatting.None,
        ContractResolver = new PrivateMembersResolver()
    };

    public string Serialize(object obj)
    {
        if (obj is null) throw new ArgumentNullException(nameof(obj));
        return JsonConvert.SerializeObject(obj, _settings);
    }

    public object Deserialize(string data, Type type)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (type is null) throw new ArgumentNullException(nameof(type));

        return JsonConvert.DeserializeObject(data, type, _settings)
               ?? throw new InvalidOperationException("JSON deserialization returned null.");
    }

    public T Deserialize<T>(string data) where T : new()
    {
        return JsonConvert.DeserializeObject<T>(data, _settings)
               ?? throw new InvalidOperationException("JSON deserialization returned null.");
    }

    private sealed class PrivateMembersResolver : DefaultContractResolver
    {
        public PrivateMembersResolver()
        {
            DefaultMembersSearchFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        }

        protected override IList<Newtonsoft.Json.Serialization.JsonProperty> CreateProperties(
     Type type,
     MemberSerialization memberSerialization)
        {
            var props = base.CreateProperties(type, memberSerialization);
            foreach (var p in props) { p.Readable = true; p.Writable = true; }
            return props;
        }

    }
}
