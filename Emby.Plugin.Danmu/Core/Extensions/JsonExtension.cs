using System;
using System.IO;
using System.Threading.Tasks;
using MediaBrowser.Model.Serialization;

namespace Emby.Plugin.Danmu.Core.Extensions
{
    public static class JsonExtension
    {
        private static IJsonSerializer _jsonSerializer;

        public static void Initialize(IJsonSerializer jsonSerializer)
        {
            _jsonSerializer = jsonSerializer;
        }

        public static string ToJson(this object obj)
        {
            if (obj == null) return string.Empty;
            if (_jsonSerializer == null)
            {
                throw new InvalidOperationException("JsonExtension not initialized. Call Initialize first.");
            }
            
            return _jsonSerializer.SerializeToString(obj);
        }

        public static Task<T> ReadFromJsonAsync<T>(this Stream content)
        {
            if (_jsonSerializer == null)
            {
                throw new InvalidOperationException("JsonExtension not initialized. Call Initialize first.");
            }
            return _jsonSerializer.DeserializeFromStreamAsync<T>(content);
        }

        public static T FromJson<T>(this string str)
        {
            if (string.IsNullOrEmpty(str)) return default(T);
            if (_jsonSerializer == null)
            {
                throw new InvalidOperationException("JsonExtension not initialized. Call Initialize first.");
            }

            try
            {
                return _jsonSerializer.DeserializeFromString<T>(str);
            }
            catch (Exception)
            {
                return default(T);
            }
        }
    }
}

