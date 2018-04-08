
namespace CsprojManager
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.IO;
    using System.Xml;

    class Program
    {
        const string StyleCopCommand = "SetStyleCop";
        const string OutputFolderCommand = "SetOutputFolder";

        const string StyleCopTargetFileName = "StyleCop.Targets";
        const string CsharpTargetFileName = "Microsoft.CSharp.targets";

        static void Main(string[] args)
        {
            var command = args[0];
            if (command.Equals(StyleCopCommand, StringComparison.InvariantCultureIgnoreCase))
            {
                HandleStyleCopCommand(args);
            }

            if (command.Equals(OutputFolderCommand, StringComparison.InvariantCultureIgnoreCase))
            {
                HandleOutputFolderCommand(args);
            }
        }

        private static void HandleStyleCopCommand(string[] args)
        {
            var solutionFolder = args[1];
            var styleCopReference = args[2];
            var treatWarningsAsErrors = args.Length >= 4 && args[3] == "1" ? true : false;
            var settingsFile = args.Length >= 5 ? args[4] : null;

            if (!Directory.Exists(solutionFolder))
            {
                Console.WriteLine("Folder passed as argument doesn't exists or is not a valid Visual Studio solution folder.");
                return;
            }

            if (!styleCopReference.EndsWith(StyleCopTargetFileName))
            {
                Console.WriteLine($"Invalid StyleCop target, it must end in \"${StyleCopTargetFileName}\".");
                return;
            }

            var files = Directory.GetFiles(solutionFolder, "*.csproj", SearchOption.AllDirectories);
            foreach (var f in files)
            {
                var doc = new XmlDocument();
                doc.Load(f);

                var nsmgr = new XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("d", "http://schemas.microsoft.com/developer/msbuild/2003");

                // Add Import element with targeting to the specified one
                AddImportWithStyleCopTarget(doc, nsmgr, f, styleCopReference);

                // Add StyleCop element StyleCopTreatErrorsAsWarnings to csproj
                AddTreatAsErrorElement(doc, nsmgr, f, treatWarningsAsErrors);

                doc.Save(f);

                // Add StyleCop configuration file to project folder
                if (settingsFile != null)
                {
                    AddStyleCopSettingFile(f, settingsFile);
                }
            }
        }

        private static void AddImportWithStyleCopTarget(XmlDocument doc, XmlNamespaceManager nsmgr, string csprojFileName, string styleCopReference)
        {
            var importsCollection = doc.SelectNodes("//d:Project/d:Import", nsmgr);

            var styleCopImportNode = FindImportByProjectEndsWith(importsCollection, StyleCopTargetFileName);
            if (styleCopImportNode == null)
            {
                var cSharpTargetsImportNode = FindImportByProjectEndsWith(importsCollection, CsharpTargetFileName);
                if (cSharpTargetsImportNode == null)
                {
                    Console.WriteLine($"Unable to add StyleCop target to \"${csprojFileName}\" because Import element with target \"${CsharpTargetFileName}\" was not foud.");
                    return;
                }

                var newStyleCopNode = doc.CreateElement("Import", doc.DocumentElement.NamespaceURI);
                newStyleCopNode.SetAttribute("Project", styleCopReference);
                doc.DocumentElement.InsertAfter(newStyleCopNode, cSharpTargetsImportNode);
            }
            else
            {
                if (styleCopImportNode.Attributes["Project"] != null)
                {
                    styleCopImportNode.Attributes["Project"].Value = styleCopReference;
                }
                else
                {
                    var projectAttr = doc.CreateAttribute("Project");
                    projectAttr.Value = styleCopReference;
                    styleCopImportNode.Attributes.Append(projectAttr);
                }
            }
        }

        private static void AddTreatAsErrorElement(XmlDocument doc, XmlNamespaceManager nsmgr, string csprojFileName, bool treatWarningsAsErrors)
        {
            var propertyGroupNodes = doc.SelectNodes("//d:Project/d:PropertyGroup", nsmgr);
            XmlNode propertyGroupNode;
            var tryErrorsStyleCopNode = FindPropertyGroupContainingElement(propertyGroupNodes, "StyleCopTreatErrorsAsWarnings", out propertyGroupNode);
            var tryErrorsStyleCopNodeText = treatWarningsAsErrors ? "true" : "false";

            if (tryErrorsStyleCopNode == null)
            {
                var assemblyNameNode = FindPropertyGroupContainingElement(propertyGroupNodes, "AssemblyName", out propertyGroupNode);
                if (assemblyNameNode == null)
                {
                    Console.WriteLine($"Unable to add StyleCopTreatErrorsAsWarnings to \"${csprojFileName}\" because element \"AssemblyName\" inside \"PropertyGroup\" element was not found.");
                    return;
                }

                var newStyleCopNode = doc.CreateElement("StyleCopTreatErrorsAsWarnings", doc.DocumentElement.NamespaceURI);
                newStyleCopNode.InnerText = tryErrorsStyleCopNodeText;
                propertyGroupNode.InsertAfter(newStyleCopNode, assemblyNameNode);
            }
            else
            {
                tryErrorsStyleCopNode.InnerText = tryErrorsStyleCopNodeText;
            }
        }

        private static void AddStyleCopSettingFile(string csprojFilePath, string settingsFile)
        {
            if (settingsFile == null)
            {
                throw new ArgumentNullException($"Invalid null parameter ${nameof(settingsFile)}");
            }

            var settingsFileName = settingsFile.Substring(settingsFile.LastIndexOf("\\") + 1);
            if (settingsFileName.ToLower() != "Settings.StyleCop".ToLower() || !File.Exists(settingsFile))
            {
                Console.WriteLine("The specified StyleCop configuration file is invalid or doesn't exist.");
                return;
            }

            var settingsFileInfo = new FileInfo(settingsFile);
            var projectDirectory = new FileInfo(csprojFilePath).Directory;
            File.Copy(settingsFile, Path.Combine(projectDirectory.FullName, settingsFileInfo.Name), true);
        }

        private static XmlNode FindImportByProjectEndsWith(XmlNodeList nodeList, string endString)
        {
            return FindImportByProject(nodeList, s => s.ToLower().EndsWith(endString.ToLower()));
        }

        private static XmlNode FindImportByProject(XmlNodeList nodeList, Predicate<string> projectSelector)
        {
            XmlNode result = null;
            for (int i = 0; i < nodeList.Count; i++)
            {
                var node = nodeList[i];
                if (node.Attributes != null && node.Attributes["Project"] != null && projectSelector(node.Attributes["Project"].Value))
                {
                    result = node;
                    break;
                }
            }

            return result;
        }

        private static XmlNode FindPropertyGroupContainingElement(XmlNodeList nodeList, string elementName, out XmlNode propertyGroupNode)
        {
            XmlNode result = null;
            propertyGroupNode = null;

            for (int i = 0; i < nodeList.Count; i++)
            {
                var propertyGroupChildNodes = nodeList[i].ChildNodes;
                for (int j = 0; j < propertyGroupChildNodes.Count; j++)
                {
                    var node = propertyGroupChildNodes[j];
                    if (node.Name == elementName)
                    {
                        propertyGroupNode = nodeList[i];
                        result = node;
                        break;
                    }

                }
            }

            return result;
        }

        private static IEnumerable<XmlNode> FindMultiplePropertyGroupContainingElement(XmlNodeList nodeList, string elementName)
        {
            for (int i = 0; i < nodeList.Count; i++)
            {
                var propertyGroupChildNodes = nodeList[i].ChildNodes;
                for (int j = 0; j < propertyGroupChildNodes.Count; j++)
                {
                    var node = propertyGroupChildNodes[j];
                    if (node.Name == elementName)
                    {
                        yield return node;
                    }

                }
            }
        }

        private static void HandleOutputFolderCommand(string[] args)
        {
            var solutionFolder = args[1];
            var outputReference = args[2];

            if (!Directory.Exists(solutionFolder))
            {
                Console.WriteLine("Folder passed as argument doesn't exists or is not a valid Visual Studio solution folder.");
                return;
            }

            var files = Directory.GetFiles(solutionFolder, "*.infrastructure.*.csproj", SearchOption.AllDirectories)
                .Except(Directory.GetFiles(solutionFolder, "*.infrastructure.core*.csproj", SearchOption.AllDirectories));

            foreach (var f in files)
            {
                var doc = new XmlDocument();
                doc.Load(f);

                var nsmgr = new XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("d", "http://schemas.microsoft.com/developer/msbuild/2003");

                EditOuputPathProperties(doc, nsmgr, f, outputReference);

                doc.Save(f);
            }
        }

        private static void EditOuputPathProperties(XmlDocument doc, XmlNamespaceManager nsmgr, string csprojFileName, string outputReference)
        {
            var propertyGroupNodes = doc.SelectNodes("//d:Project/d:PropertyGroup", nsmgr);
            foreach (var outputPathNode in FindMultiplePropertyGroupContainingElement(propertyGroupNodes, "OutputPath"))
            {
                outputPathNode.InnerText = outputReference;
            }
        }
    }
}
