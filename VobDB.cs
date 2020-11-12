using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using ProtoBuf;

namespace GitImporter
{
    [Serializable]
    [ProtoContract]
    public class VobDb
    {
        [ProtoMember(1)]
        private List<Element> _rawElements;

        public VobDb(Dictionary<string, Element> elementsByOid, Dictionary<string, LabelMeta> labelMetas)
        {
            ElementsByOid = elementsByOid;
            LabelMetas = labelMetas;
        }

        public VobDb()
        {
            ElementsByOid = new Dictionary<string, Element>();
        }

        public Dictionary<string, Element> ElementsByOid { get; private set; }

        [ProtoMember(2)]
        public Dictionary<string, LabelMeta> LabelMetas { get; private set; }

        public void Add(VobDb other)
        {
            foreach(var pair in other.ElementsByOid)
            {
                Element existing;
                if(!ElementsByOid.TryGetValue(pair.Key, out existing))
                {
                    ElementsByOid.Add(pair.Key, pair.Value);
                    continue;
                }

                // TODO : we should keep the one with the most versions/branches
                if(existing.Name != pair.Value.Name)
                    Program.Logger.TraceData(TraceEventType.Information,
                                             0,
                                             $"element with oid {existing.Oid} has a different name : keeping {existing.Name}, ignoring {pair.Value.Name}");
            }
        }

        [ProtoBeforeSerialization]
        private void BeforeProtobufSerialization()
        {
            _rawElements = new List<Element>(ElementsByOid.Values);
        }

        [ProtoAfterDeserialization]
        private void AfterProtobufDeserialization()
        {
            if(_rawElements == null)
            {
                ElementsByOid = new Dictionary<string, Element>();
                return;
            }

            ElementsByOid = _rawElements.ToDictionary(e => e.Oid);
            foreach(var element in _rawElements)
            {
                if(element is SymLinkElement symlink)
                    symlink.Fixup(ElementsByOid);

                foreach(var version in element.Branches.Values.SelectMany(branch => branch.Versions.OfType<DirectoryVersion>()))
                    version.FixContent(ElementsByOid);
            }

            _rawElements = null;
        }
    }
}