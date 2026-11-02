using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ImageCare.Core.Domain.Annotations;

namespace ImageCare.Core.Services.MediaAnnotationsService;

internal sealed class XmpDocumentCodec
{
    private static readonly XNamespace XmpMetaNamespace = "adobe:ns:meta/";
    private static readonly XNamespace RdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace XmpNamespace = "http://ns.adobe.com/xap/1.0/";
    private static readonly XNamespace DcNamespace = "http://purl.org/dc/elements/1.1/";

    public async Task<XDocument> LoadAsync(Stream input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            CloseInput = false
        };
        using var reader = XmlReader.Create(input, settings);
        return await XDocument.LoadAsync(reader, LoadOptions.PreserveWhitespace, cancellationToken)
            .ConfigureAwait(false);
    }

    public MediaAnnotations ReadAnnotations(XDocument document)
    {
        var descriptions = document.Descendants(RdfNamespace + "Description").ToArray();
        var ratingValue = descriptions
            .Select(description => (string?)description.Attribute(XmpNamespace + "Rating"))
            .Concat(descriptions.SelectMany(description =>
                description.Elements(XmpNamespace + "Rating")
                    .Select(element => element.Element(RdfNamespace + "value")?.Value ?? element.Value)))
            .FirstOrDefault(value => int.TryParse(value, out var parsed) && parsed is >= 0 and <= 5);
        var rating = int.TryParse(ratingValue, out var parsedRating) ? parsedRating : 0;
        var tags = descriptions
            .SelectMany(description => description.Elements(DcNamespace + "subject"))
            .SelectMany(subject => subject.Descendants(RdfNamespace + "li"))
            .Select(ReadListItemValue);
        return new MediaAnnotations(rating, tags);
    }

    public void WriteAnnotations(XDocument document, MediaAnnotations annotations)
    {
        var rdf = document.Descendants(RdfNamespace + "RDF").FirstOrDefault();
        if (rdf is null)
        {
            var root = document.Root ?? new XElement(XmpMetaNamespace + "xmpmeta");
            if (document.Root is null)
            {
                document.Add(root);
            }

            rdf = new XElement(RdfNamespace + "RDF");
            root.Add(rdf);
        }

        var description = rdf.Elements(RdfNamespace + "Description").FirstOrDefault();
        if (description is null)
        {
            description = new XElement(
                RdfNamespace + "Description",
                new XAttribute(RdfNamespace + "about", string.Empty));
            rdf.Add(description);
        }

        // XMP permits parallel standard representations in separate descriptions. ImageCare
        // updates only the first representation selected by its reader and leaves alternates,
        // qualifiers, and application-owned duplicates represented in the XML document.
        var ratingAttribute = document.Descendants(RdfNamespace + "Description")
            .Attributes(XmpNamespace + "Rating")
            .FirstOrDefault();
        var ratingElement = document.Descendants(RdfNamespace + "Description")
            .Elements(XmpNamespace + "Rating")
            .FirstOrDefault();
        if (ratingAttribute is not null)
        {
            ratingAttribute.Value = annotations.Rating.ToString(CultureInfo.InvariantCulture);
        }
        else if (ratingElement is not null)
        {
            var qualifiedValue = ratingElement.Element(RdfNamespace + "value");
            if (qualifiedValue is not null)
            {
                qualifiedValue.Value = annotations.Rating.ToString(CultureInfo.InvariantCulture);
            }
            else if (!ratingElement.HasElements)
            {
                ratingElement.Value = annotations.Rating.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                ratingElement.AddFirst(new XElement(
                    RdfNamespace + "value",
                    annotations.Rating.ToString(CultureInfo.InvariantCulture)));
            }
        }
        else
        {
            description.SetAttributeValue(XmpNamespace + "Rating", annotations.Rating);
        }

        var subject = document.Descendants(RdfNamespace + "Description")
            .Elements(DcNamespace + "subject")
            .FirstOrDefault();
        if (subject is null)
        {
            subject = new XElement(DcNamespace + "subject");
            description.Add(subject);
        }

        var container = subject.Elements().FirstOrDefault(element =>
            element.Name == RdfNamespace + "Bag"
            || element.Name == RdfNamespace + "Seq"
            || element.Name == RdfNamespace + "Alt");
        if (container is null)
        {
            container = new XElement(RdfNamespace + "Bag");
            subject.Add(container);
        }

        var existingItems = container.Elements(RdfNamespace + "li").ToArray();
        var retainedItems = new HashSet<XElement>();
        var replacementItems = annotations.Tags.Select(tag =>
        {
            var retained = existingItems.FirstOrDefault(item =>
                !retainedItems.Contains(item)
                && string.Equals(ReadListItemValue(item), tag, StringComparison.OrdinalIgnoreCase));
            if (retained is not null)
            {
                retainedItems.Add(retained);
                return retained;
            }

            return new XElement(RdfNamespace + "li", tag);
        }).ToArray();

        // Replace only direct list members of the selected subject. Unchanged values retain the
        // original li node, including xml:lang, rdf:* attributes, and nested qualifiers. Parallel
        // dc:subject elements remain untouched and the reader intentionally unions their values.
        existingItems.Remove();
        container.Add(replacementItems);
    }

    public XDocument CreateDocument() =>
        new(
            new XElement(
                XmpMetaNamespace + "xmpmeta",
                new XAttribute(XNamespace.Xmlns + "x", XmpMetaNamespace),
                new XElement(
                    RdfNamespace + "RDF",
                    new XAttribute(XNamespace.Xmlns + "rdf", RdfNamespace),
                    new XElement(
                        RdfNamespace + "Description",
                        new XAttribute(RdfNamespace + "about", string.Empty),
                        new XAttribute(XNamespace.Xmlns + "xmp", XmpNamespace),
                        new XAttribute(XNamespace.Xmlns + "dc", DcNamespace)))));

    public byte[] Serialize(XDocument document)
    {
        using var output = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            OmitXmlDeclaration = false,
            CloseOutput = false
        };
        using (var writer = XmlWriter.Create(output, settings))
        {
            document.WriteTo(writer);
        }

        return output.ToArray();
    }

    private static string ReadListItemValue(XElement item) =>
        item.Element(RdfNamespace + "value")?.Value
        ?? item.Nodes().OfType<XText>().FirstOrDefault()?.Value
        ?? item.Value;
}
