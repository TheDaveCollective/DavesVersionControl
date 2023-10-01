using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;

namespace DavesVersionControl
{

    public class XMLDiffrence
    {
        private List<String> ignoreAttributes = new List<string> { "id","classid" };

        private Queue<string> Traces = new Queue<string>();
        int maxtraces = 50;
        string lastrace = "";
        int skipped = 0;

        static bool traceon = false;
        private void Trace(string message) //we don't log the same message twice in a row
        {
            if (traceon)
            {
                if (lastrace != message)
                {
                    lastrace = message;

                    if (skipped > 0)
                    {
                        message = $"skipped {skipped} : {message}";
                    }

                    Traces.Enqueue($"{message}");
                    if (Traces.Count > maxtraces)
                    {
                        Traces.Dequeue();
                    }
                    skipped = 0;
                }
                else
                {
                    skipped++;
                }
            }

        }
        private void FlushTrace(ITracingService tracingService,string message)
        {
            tracingService.Trace($"XMLDiffrence FlushTrace START (oldest is firsT) {message}");
            var count = Traces.Count;
            while (Traces.Count>0)
            {
                var trace = Traces.Dequeue();
                tracingService.Trace($"{count--} XMLDiffrence {trace}");
            }
            tracingService.Trace($"XMLDiffrence FlushTrace END  {message}");
        }
        public XDocument FindXmlDifferences(ITracingService tracingService,string xml1, string xml2)
        {
            try
            {
                if (!string.IsNullOrEmpty(xml1))
                {
                    tracingService.Trace($"FindXmlDifferences xml1 was null or empty");
                }
                if (!string.IsNullOrEmpty(xml2))
                {
                    tracingService.Trace($"FindXmlDifferences xml2 was null or empty");
                }
                if (xml1.Length > 200)
                {
                    Trace(xml1.Substring(0, 200));
                }
                else
                {
                    Trace(xml1);
                }
                if (xml2.Length > 200)
                {
                    Trace(xml2.Substring(0, 200));
                }
                else
                {
                    Trace(xml2);
                }


                // Parse the XML documents using XDocument.
                XDocument doc1 = XDocument.Parse(xml1);
                XDocument doc2 = XDocument.Parse(xml2);

                // Find differences between the two documents.
                XDocument diffDocument = CreateDiffDocument(tracingService,doc1.Root, doc2.Root);

                return diffDocument;
            }
            catch (Exception ex)
            {
                traceon = true;  //well next time we might get it
                tracingService.Trace($"FindXmlDifferences: Error: {ex.Message}");
                FlushTrace(tracingService, $"FindXmlDifferences: Error: {ex.Message} {ex.StackTrace} ");
                var inner = ex.InnerException;
                while(inner != null)
                {
                    tracingService.Trace($"FindXmlDifferences: inner Error: {ex.Message} {ex.StackTrace}");
                    inner = ex.InnerException;
                }

                return null;
            }
        }

        private XDocument CreateDiffDocument(ITracingService tracingService,XElement elem1, XElement elem2)
        {
            XDocument diffDocument = new XDocument(
                new XElement("Differences",
                    FindDifferences(tracingService,elem1, elem2).Select(difference =>
                        new XElement("Difference", difference)
                    )
                )
            );

            return diffDocument;
        }
        
        

        private IEnumerable<string> FindDifferences(ITracingService tracingService,XElement elem1, XElement elem2)
        {
            var elem1path = BuildPathToRoot(elem1);
            var elem2path = BuildPathToRoot(elem2);
            Trace($" current element {elem1path}");
            Trace($" ELEMENT1 {elem1.ToString()}");
            Trace($" ELEMENT2 {elem2.ToString()}");

            if (elem1path != elem2path)
            {
                tracingService.Trace($" Element paths are diffrent  {elem1path} {elem2path}");
            }
            Trace("elm1/2");
            //tracingService.Trace($" path to element {elem1path}");
            // Compare element names.
            if (elem1.Name != elem2.Name)
            {

                yield return $"Element name '{elem1.Name}':'{elem1.Value}' is different from '{elem2.Name}':'{elem2.Value}'";
            }
            Trace("if (elem1.Name != elem2.Name)");
            // Compare attributes.
            var attributes1 = elem1.Attributes().ToList();
            var attributes2 = elem2.Attributes().ToList();
            
            foreach (var attr1 in attributes1)
            {
                Trace("foreach (var attr1 in attributes1)");
                var matchingAttr = attributes2.FirstOrDefault(attr2 => attr2.Name == attr1.Name);

                if (matchingAttr == null || matchingAttr.Value != attr1.Value)
                {
                    if (!ignoreAttributes.Contains($"{attr1.Name}"))
                    {
                        yield return $"Attribute '{attr1.Name}':'{attr1.Value}' value is different '{matchingAttr.Value}'";
                    }
                }
                else
                {
                    attributes2.Remove(matchingAttr);
                }
            }

            foreach (var attr2 in attributes2)
            {
                if (!ignoreAttributes.Contains($"{attr2.Name}"))
                {
                    yield return $"Attribute '{attr2.Name}':'{attr2.Value}' is missing in the first document";
                }

            }

            // Compare child elements recursively.
            var elements1 = elem1.Elements().ToList();
            var elements2 = elem2.Elements().ToList();

            if (elements1.Count != elements2.Count)
            {
                yield return $"Number of child elements for {elem1.Name}:{elements1.Count} {elem2.Name}:{elements2.Count} is different path {elem1path}";
            }
            Trace("if (elements1.Count != elements2.Count)");


            foreach (var child1 in elements1)
            {
                Trace(" foreach (var child1 in elements1) ");
                var matchingChild = elements2.FirstOrDefault(child2 => child2.Name == child1.Name);

                if (matchingChild == null)
                {
                    yield return $"Child element '{child1.Name}':'{child1.Value}' is missing in the New document";
                }
                else
                {
                    elements2.Remove(matchingChild);

                    foreach (var difference in FindDifferences(tracingService,child1, matchingChild))
                    {
                        yield return difference;
                    }
                }
            }
            /*
            Trace(" try reverse check... 2->1 ");
            foreach (var child2 in elements2)
            {
                Trace(" foreach (var child2 in elements2) ");
                var matchingChild = elements1.FirstOrDefault(child1 => child1.Name == child2.Name);

                if (matchingChild == null)
                {
                    yield return $"Child element '{elem2path}\\{child2.Name}':'{child2.Value}' is Added in the New document";
                }
                else
                {
                    elements1.Remove(matchingChild);

                    foreach (var difference in FindDifferences(tracingService, child2, matchingChild))
                    {
                        yield return difference;
                    }
                }
            }*/
            Trace("all fine ");
        }
        string BuildPathToRoot(XElement element)
        {
            string path = element.Name.LocalName;

            // Traverse up the XML hierarchy to the root.
            foreach (XElement ancestor in element.Ancestors())
            {
                path = ancestor.Name.LocalName + "/" + path;
            }

            return "/" + path; // Add the root element at the beginning.
        }
    }


}
