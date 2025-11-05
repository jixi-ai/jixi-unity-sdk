using System;
using UnityEngine;

namespace JixiAI
{
    public static class JsonHelper
    {
        [Serializable] private class Wrapper<T> { public T[] items; }
        public static T[] FromJsonArray<T>(string json)
        {
            var wrapped = "{\"items\":" + json + "}";
            return JsonUtility.FromJson<Wrapper<T>>(wrapped).items;
        }
    }
}