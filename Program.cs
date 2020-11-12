using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using CommandLine;

using ProtoBuf;

namespace GitImporter
{
    public enum TraceId
    {
        ReadExport = 1,
        ReadCleartool,
        CreateChangeSet,
        ApplyChangeSet,
        Cleartool
    }

    class Program
    {
        public static TraceSource Logger = new TraceSource("GitImporter", SourceLevels.All);

        static int Main(string[] args)
        {
            Console.Error.WriteLine("GitImporter called with {0} arguments :", args.Length);
            foreach(string arg in args)
                Console.Error.WriteLine("    " + arg);
            Logger.TraceData(TraceEventType.Information, 0, $"GitImporter called with {args.Length} arguments : {string.Join(" ", args)}");
            var importerArguments = new ImporterArguments();
            if(!Parser.ParseArgumentsWithUsage(args, importerArguments))
                return 1;
            if(!importerArguments.CheckArguments())
            {
                Console.Error.WriteLine(Parser.ArgumentsUsage(typeof(ImporterArguments)));
                return 1;
            }

            try
            {
                Logger.TraceData(TraceEventType.Start | TraceEventType.Information, 0, "Start program");
                VobDb vobDb = null;

                if(!string.IsNullOrEmpty(importerArguments.FetchFileContent))
                {
                    using(var gitWriter = new GitWriter(importerArguments.ClearcaseRoot, importerArguments.NoFileContent, importerArguments.Labels, importerArguments.Roots))
                    {
                        if(File.Exists(importerArguments.ThirdpartyConfig))
                        {
                            var thirdPartyConfig = ThirdPartyConfig.ReadFromFile(importerArguments.ThirdpartyConfig);
                            var hook = new ThirdPartyHook(thirdPartyConfig);
                            gitWriter.PreWritingHooks.AddRange(hook.PreWritingHooks);
                            gitWriter.PostWritingHooks.AddRange(hook.PostWritingHooks);
                        }

                        gitWriter.WriteFile(importerArguments.FetchFileContent);
                    }

                    Logger.TraceData(TraceEventType.Stop | TraceEventType.Information, 0, "Stop program");
                    return 0;
                }

                if(importerArguments.LoadVobDb != null && importerArguments.LoadVobDb.Length > 0)
                {
                    foreach(string vobDbFile in importerArguments.LoadVobDb)
                    {
                        using(var stream = new FileStream(vobDbFile, FileMode.Open))
                            if(vobDb == null)
                                vobDb = Serializer.Deserialize<VobDb>(stream);
                            else
                                vobDb.Add(Serializer.Deserialize<VobDb>(stream));
                        Logger.TraceData(TraceEventType.Information, 0, "Clearcase data successfully loaded from " + vobDbFile);
                    }
                }

                var exportReader = new ExportReader(importerArguments.OriginDate, importerArguments.Labels);
                foreach(var file in importerArguments.ExportFiles)
                    exportReader.ReadFile(file);

                List<ElementVersion> newVersions = null;

                if(!string.IsNullOrWhiteSpace(importerArguments.DirectoriesFile) ||
                   !string.IsNullOrWhiteSpace(importerArguments.ElementsFile) ||
                   !string.IsNullOrWhiteSpace(importerArguments.VersionsFile))
                    using(var cleartoolReader = new CleartoolReader(importerArguments.ClearcaseRoot, importerArguments.OriginDate, importerArguments.Labels))
                    {
                        cleartoolReader.Init(vobDb, exportReader.Elements);
                        // first save of exportReader with oid (if something was actually read)
                        vobDb = cleartoolReader.VobDb;
                        if(importerArguments.ExportFiles.Length > 0 && !string.IsNullOrWhiteSpace(importerArguments.SaveVobDb))
                        {
                            using(var stream = new FileStream(importerArguments.SaveVobDb + ".export_oid", FileMode.Create))
                                Serializer.Serialize(stream, vobDb);
                            Logger.TraceData(TraceEventType.Information, 0, "Clearcase export with oid successfully saved in " + importerArguments.SaveVobDb + ".export_oid");
                        }

                        newVersions = cleartoolReader.Read(importerArguments.DirectoriesFile, importerArguments.ElementsFile, importerArguments.VersionsFile);
                        vobDb = cleartoolReader.VobDb;
                        if(!string.IsNullOrWhiteSpace(importerArguments.SaveVobDb))
                        {
                            using(var stream = new FileStream(importerArguments.SaveVobDb, FileMode.Create))
                                Serializer.Serialize(stream, vobDb);
                            Logger.TraceData(TraceEventType.Information, 0, "Clearcase data successfully saved in " + importerArguments.SaveVobDb);
                        }
                    }

                if(!importerArguments.GenerateVobDbOnly)
                {
                    HistoryBuilder historyBuilder = null;
                    // we only use an existing HistoryBuilder for incremental import, ie when newVersions != null
                    if(newVersions != null && !string.IsNullOrWhiteSpace(importerArguments.History) && File.Exists(importerArguments.History))
                    {
                        using(var stream = new FileStream(importerArguments.History, FileMode.Open))
                            historyBuilder = Serializer.Deserialize<HistoryBuilder>(stream);
                        Logger.TraceData(TraceEventType.Information, 0, "History data successfully loaded from " + importerArguments.History);
                        historyBuilder.Fixup(vobDb);
                    }

                    if(historyBuilder == null)
                        historyBuilder = new HistoryBuilder(vobDb);

                    // command-line arguments take precedence
                    historyBuilder.SetRoots(importerArguments.ClearcaseRoot, importerArguments.Roots);
                    historyBuilder.SetBranchFilters(importerArguments.Branches);

                    var changeSets = historyBuilder.Build(newVersions);
                    var branchRename = historyBuilder.GetBranchRename();
                    var labels = historyBuilder.GetLabels();

                    using(var gitWriter = new GitWriter(importerArguments.ClearcaseRoot, importerArguments.NoFileContent, importerArguments.Labels, importerArguments.Roots, branchRename))
                    {
                        if(File.Exists(importerArguments.IgnoreFile))
                            gitWriter.InitialFiles.Add(new Tuple<string, string>(".gitignore", importerArguments.IgnoreFile));
                        if(File.Exists(importerArguments.ThirdpartyConfig))
                        {
                            var thirdPartyConfig = ThirdPartyConfig.ReadFromFile(importerArguments.ThirdpartyConfig);
                            var hook = new ThirdPartyHook(thirdPartyConfig);
                            gitWriter.PreWritingHooks.AddRange(hook.PreWritingHooks);
                            gitWriter.PostWritingHooks.AddRange(hook.PostWritingHooks);
                            gitWriter.InitialFiles.Add(new Tuple<string, string>(".gitmodules", hook.ModulesFile));
                        }

                        gitWriter.WriteChangeSets(changeSets, labels, vobDb.LabelMetas);
                    }

                    if(!string.IsNullOrWhiteSpace(importerArguments.History))
                    {
                        if(File.Exists(importerArguments.History))
                            File.Move(importerArguments.History, importerArguments.History + ".bak");
                        using(var stream = new FileStream(importerArguments.History, FileMode.Create))
                            Serializer.Serialize(stream, historyBuilder);
                        Logger.TraceData(TraceEventType.Information, 0, "History data successfully saved in " + importerArguments.History);
                    }
                }

                return 0;
            }
            catch(Exception ex)
            {
                Logger.TraceData(TraceEventType.Critical, 0, "Exception during import : " + ex);
                Console.Error.WriteLine("Exception during import : " + ex);
                return 1;
            }
            finally
            {
                Logger.TraceData(TraceEventType.Stop | TraceEventType.Information, 0, "Stop program");
                Logger.Flush();
            }
        }
    }
}