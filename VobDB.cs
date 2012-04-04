﻿using System;
using System.Collections.Generic;
using System.Linq;
using ProtoBuf;

namespace GitImporter
{
    [Serializable]
    [ProtoContract]
    public class VobDB
    {
        public Dictionary<string, Element> ElementsByOid { get; private set; }

        public VobDB(Dictionary<string, Element> elementsByOid)
        {
            ElementsByOid = elementsByOid;
        }

        public VobDB()
        {
            ElementsByOid = new Dictionary<string, Element>();
        }

        public void Add(VobDB other)
        {
            foreach (var pair in other.ElementsByOid)
            {
                Element existing;
                if (!ElementsByOid.TryGetValue(pair.Key, out existing))
                {
                    ElementsByOid.Add(pair.Key, pair.Value);
                    continue;
                }
                if (existing.Name != pair.Value.Name)
                    throw new Exception(string.Format("Name mismatchElement with oid {0} : {1} != {2}", existing.Oid, existing.Name, pair.Value.Name));
            }
        }

        [ProtoMember(1)]
        private List<Element> _rawElements;

        [ProtoBeforeSerialization]
        private void BeforeProtobufSerialization()
        {
            _rawElements = new List<Element>(ElementsByOid.Values);
        }

        [ProtoAfterDeserialization]
        private void AfterProtobufDeserialization()
        {
            if (_rawElements == null)
            {
                ElementsByOid = new Dictionary<string, Element>();
                return;
            }
            ElementsByOid = _rawElements.ToDictionary(e => e.Oid);
            foreach (var element in _rawElements)
                foreach (var branch in element.Branches.Values)
                    foreach (var version in branch.Versions.OfType<DirectoryVersion>())
                        version.FixContent(ElementsByOid);
            _rawElements = null;
        }
    }
}
