﻿using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace EmberDataAdapter
{
    internal static class EdGraphRewriter
    {
        public static JObject Deconstruct(JToken root)
        {
            var workingSet = new Dictionary<string, List<JObject>>();

            if (root.Type != JTokenType.Object && root.Type != JTokenType.Array)
            {
                throw new ArgumentException("Ember Data JSON serialization can only serialize objects and collections of objects.");
            }

            // Extract object roots from any top level collections.
            var roots = new List<JObject>();
            if (root.Type == JTokenType.Array)
            {
                if (root.Children().Any(c => c.Type != JTokenType.Object))
                {
                    throw new ArgumentException("Ember Data JSON serialization can only serialize objects and collections of objects.");                    
                }
                roots.AddRange(root.Children<JObject>());
            }
            else
            {
                roots.Add(root as JObject);
            }

            // Deconstruct all object roots.
            foreach (var r in roots)
            {
                DeconstructImpl(workingSet, r);
            }            

            // Aggregate all objects into type-based collections
            var newRoot = new JObject();
            foreach (var typeCollection in workingSet)
            {
                var typeArray = new JArray(typeCollection.Value.ToArray());
                newRoot.Add(typeCollection.Key, typeArray);
            }

            // Remove $type properties
            foreach (JObject c in newRoot.Children().SelectMany(c => c.Children()).SelectMany(c => c.Children()))
            {
                c.Remove("$type");
            }

            return newRoot;
        }

        private static void DeconstructImpl(Dictionary<string, List<JObject>> workingSet, JObject localRoot)
        {
            var name = GetEdTypeName(localRoot, true);

            // Create a type collection as necessary
            List<JObject> typeCollection;
            if(!workingSet.TryGetValue(name, out typeCollection))
            {
                typeCollection = new List<JObject>();
                workingSet.Add(name, typeCollection);
            }
            typeCollection.Add(localRoot);

            // Recursively locate all type definitions
            var directObjectRefs = localRoot.Properties()
                                            .Where(p => p.Value.Type == JTokenType.Object)
                                            .Select(p => p.Value)
                                            .Cast<JObject>();
            var arrayObjectRefs = localRoot.Properties()
                                           .Where(p => p.Value.Type == JTokenType.Array)
                                           .Select(p => p.Value)
                                           .Cast<JArray>()
                                           .SelectMany(a => a.Children())
                                           .Where(v => v.Type == JTokenType.Object)
                                           .Cast<JObject>();
  

            foreach (var o in arrayObjectRefs.Union(directObjectRefs))
            {
                DeconstructImpl(workingSet, o);
            }

            // Indirect object references
            var frozenProperties = localRoot.Properties().ToArray();
            foreach (var p in frozenProperties.Where(p => p.Value.Type == JTokenType.Object))
            {
                localRoot.Remove(p.Name);
                localRoot.Add(EdUtil.GetEdReferenceObjectName(p.Name), GetEdIdFieldValue((JObject) p.Value));
            }
            // Object references through arrays
            foreach (var p in frozenProperties.Where(p => p.Value.Type == JTokenType.Array))
            {
                var newArray = new JArray(p.Value.Children<JObject>().Select(GetEdIdFieldValue).ToArray());
                localRoot.Remove(p.Name);
                // References through arrays are known by the type of their contents
                var arrayType = GetEdTypeName(p.Value.Children<JObject>().First(), false);
                localRoot.Add(EdUtil.GetEdReferenceArrayName(arrayType), newArray);
            }
        }

        private static string GetEdTypeName(JObject obj, bool pluralize)
        {
            return obj.Properties().Where(p => p.Name == "$type")
                                   .Select(p => p.Value.Value<string>() + (pluralize ? "s" : ""))
                                   .First();
        }

        private static JToken GetEdIdFieldValue(JObject obj)
        {
            // TODO: Add annotation support
            const string propertyFieldName = "id";
            return obj.Properties().Where(p => p.Name == propertyFieldName).Select(p => p.Value).First();
        }
    }
}