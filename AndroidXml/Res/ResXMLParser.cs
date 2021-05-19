using System;
using System.Collections.Generic;
using System.IO;
using AndroidXml.Utils;

namespace AndroidXml.Res
{
    public class ResXMLParser
    {
        #region XmlParserEventCode enum

        public enum XmlParserEventCode
        {
            NOT_STARTED,
            BAD_DOCUMENT,
            START_DOCUMENT,
            END_DOCUMENT,
            CLOSED,

            START_NAMESPACE = ResourceType.RES_XML_START_NAMESPACE_TYPE,
            END_NAMESPACE = ResourceType.RES_XML_END_NAMESPACE_TYPE,
            START_TAG = ResourceType.RES_XML_START_ELEMENT_TYPE,
            END_TAG = ResourceType.RES_XML_END_ELEMENT_TYPE,
            TEXT = ResourceType.RES_XML_CDATA_TYPE
        }

        #endregion

        private readonly IEnumerator<XmlParserEventCode> _parserIterator;

        private readonly Stream _source;
        private List<ResXMLTree_attribute> _attributes;
        private object _currentExtension;
        private ResXMLTree_node _currentNode;
        private ResReader _reader;

        public ResXMLParser(Stream source)
        {
            _source = source;
            _reader = new ResReader(_source);
            EventCode = XmlParserEventCode.NOT_STARTED;
            _parserIterator = ParserIterator().GetEnumerator();
        }

        public ResStringPool Strings { get; private set; }

        public ResResourceMap ResourceMap { get; private set; }

        public XmlParserEventCode EventCode { get; private set; }

        public uint? CommentID => _currentNode?.Comment.Index;

        public string Comment => Strings.GetString(CommentID);

        public uint? LineNumber => _currentNode?.LineNumber;

        public uint? NamespacePrefixID
        {
            get
            {
                var namespaceExt = _currentExtension as ResXMLTree_namespaceExt;
                return namespaceExt?.Prefix.Index;
            }
        }

        public string NamespacePrefix => Strings.GetString(NamespacePrefixID);

        public uint? NamespaceUriID
        {
            get
            {
                var namespaceExt = _currentExtension as ResXMLTree_namespaceExt;
                return namespaceExt?.Uri.Index;
            }
        }

        public string NamespaceUri => Strings.GetString(NamespaceUriID);

        public uint? CDataID
        {
            get
            {
                var cdataExt = _currentExtension as ResXMLTree_cdataExt;
                return cdataExt?.Data.Index;
            }
        }

        public string CData => Strings.GetString(CDataID);

        public uint? ElementNamespaceID
        {
            get
            {
                switch (_currentExtension)
                {
                    case ResXMLTree_attrExt attrExt:
                        return attrExt.Namespace.Index;
                    case ResXMLTree_endElementExt endElementExt:
                        return endElementExt.Namespace.Index;
                    default:
                        return null;
                }
            }
        }

        public string ElementNamespace => Strings.GetString(ElementNamespaceID);

        public uint? ElementNameID
        {
            get
            {
                switch (_currentExtension)
                {
                    case ResXMLTree_attrExt attrExt:
                        return attrExt.Name.Index;
                    case ResXMLTree_endElementExt endElementExt:
                        return endElementExt.Name.Index;
                    default:
                        return null;
                }
            }
        }

        public string ElementName => Strings.GetString(ElementNameID);

        public uint? ElementIdIndex
        {
            get
            {
                switch (_currentExtension)
                {
                    case ResXMLTree_attrExt attrExt:
                        return attrExt.IdIndex;
                    default:
                        return null;
                }
            }
        }

        public AttributeInfo ElementId => GetAttribute(ElementIdIndex);

        public uint? ElementClassIndex
        {
            get
            {
                switch (_currentExtension)
                {
                    case ResXMLTree_attrExt attrExt:
                        return attrExt.ClassIndex;
                    default:
                        return null;
                }
            }
        }

        public AttributeInfo ElementClass => GetAttribute(ElementClassIndex);

        public uint? ElementStyleIndex
        {
            get
            {
                switch (_currentExtension)
                {
                    case ResXMLTree_attrExt attrExt:
                        return attrExt.StyleIndex;
                    default:
                        return null;
                }
            }
        }

        public AttributeInfo ElementStyle => GetAttribute(ElementStyleIndex);

        public uint AttributeCount => _attributes == null ? 0 : (uint)_attributes.Count;

        public void Restart()
        {
            throw new NotSupportedException();
        }

        public XmlParserEventCode Next()
        {
            if (_parserIterator.MoveNext())
            {
                EventCode = _parserIterator.Current;
                return _parserIterator.Current;
            }

            EventCode = XmlParserEventCode.END_DOCUMENT;
            return EventCode;
        }

        private void ClearState()
        {
            _currentNode = null;
            _currentExtension = null;
            _attributes = null;
        }

        private IEnumerable<XmlParserEventCode> ParserIterator()
        {
            while (true)
            {
                ClearState();
                ResChunk_header header;
                try
                {
                    header = _reader.ReadResChunk_header();
                }
                catch (EndOfStreamException)
                {
                    break;
                }

                var subStream = new BoundedStream(_reader.BaseStream, header.Size - 8);
                var subReader = new ResReader(subStream);
                switch (header.Type)
                {
                    case ResourceType.RES_XML_TYPE:
                        yield return XmlParserEventCode.START_DOCUMENT;
                        _reader = subReader; // Bound whole file
                        continue; // Don't skip content
                    case ResourceType.RES_STRING_POOL_TYPE:
                        var stringPoolHeader = subReader.ReadResStringPool_header(header);
                        Strings = subReader.ReadResStringPool(stringPoolHeader);
                        break;
                    case ResourceType.RES_XML_RESOURCE_MAP_TYPE:
                        var resourceMap = subReader.ReadResResourceMap(header);
                        ResourceMap = resourceMap;
                        break;
                    case ResourceType.RES_XML_START_NAMESPACE_TYPE:
                        _currentNode = subReader.ReadResXMLTree_node(header);
                        _currentExtension = subReader.ReadResXMLTree_namespaceExt();
                        yield return XmlParserEventCode.START_NAMESPACE;
                        break;
                    case ResourceType.RES_XML_END_NAMESPACE_TYPE:
                        _currentNode = subReader.ReadResXMLTree_node(header);
                        _currentExtension = subReader.ReadResXMLTree_namespaceExt();
                        yield return XmlParserEventCode.END_NAMESPACE;
                        break;
                    case ResourceType.RES_XML_START_ELEMENT_TYPE:
                        _currentNode = subReader.ReadResXMLTree_node(header);
                        var attrExt = subReader.ReadResXMLTree_attrExt();
                        _currentExtension = attrExt;

                        _attributes = new List<ResXMLTree_attribute>();
                        for (var i = 0; i < attrExt.AttributeCount; i++)
                        {
                            _attributes.Add(subReader.ReadResXMLTree_attribute());
                        }

                        yield return XmlParserEventCode.START_TAG;
                        break;
                    case ResourceType.RES_XML_END_ELEMENT_TYPE:
                        _currentNode = subReader.ReadResXMLTree_node(header);
                        _currentExtension = subReader.ReadResXMLTree_endElementExt();
                        yield return XmlParserEventCode.END_TAG;
                        break;
                    case ResourceType.RES_XML_CDATA_TYPE:
                        _currentNode = subReader.ReadResXMLTree_node(header);
                        _currentExtension = subReader.ReadResXMLTree_cdataExt();
                        yield return XmlParserEventCode.TEXT;
                        break;
                    default:
                        Console.WriteLine("Warning: Skipping chunk of type {0} (0x{1:x4})",
                            header.Type, (int)header.Type);
                        break;
                }

                var junk = subStream.ReadFully();
                if (junk.Length > 0)
                {
                    Console.WriteLine("Warning: Skipping {0} bytes at the end of a {1} (0x{2:x4}) chunk.",
                        junk.Length, header.Type, (int)header.Type);
                }
            }
        }

        public AttributeInfo GetAttribute(uint? index)
        {
            if (index == null || _attributes == null)
            {
                return null;
            }

            if (index >= _attributes.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var attr = _attributes[(int)index];
            return new AttributeInfo(this, attr);
        }

        public uint? IndexOfAttribute(string ns, string attribute)
        {
            var nsID = Strings.IndexOfString(ns);
            var nameID = Strings.IndexOfString(attribute);
            if (nameID == null)
            {
                return null;
            }

            uint index = 0;
            foreach (var attr in _attributes)
            {
                if (attr.Namespace.Index == nsID && attr.Name.Index == nameID)
                {
                    return index;
                }

                index++;
            }

            return null;
        }

        public void Close()
        {
            if (EventCode == XmlParserEventCode.CLOSED)
            {
                return;
            }

            EventCode = XmlParserEventCode.CLOSED;
            _reader.Close();
        }

        #region Nested type: AttributeInfo

        public class AttributeInfo
        {
            private readonly ResXMLParser _parser;

            public AttributeInfo(ResXMLParser parser, ResXMLTree_attribute attribute)
            {
                _parser = parser;
                TypedValue = attribute.TypedValue;
                ValueStringID = attribute.RawValue.Index;
                NameID = attribute.Name.Index;
                NamespaceID = attribute.Namespace.Index;
            }

            public uint? NamespaceID { get; }

            public string Namespace => _parser.Strings.GetString(NamespaceID);

            public uint? NameID { get; }

            public string Name => _parser.Strings.GetString(NameID);

            public uint? ValueStringID { get; }

            public string ValueString => _parser.Strings.GetString(ValueStringID);

            public Res_value TypedValue { get; }
        }

        #endregion
    }
}