#region Copyright (c) 2019 Atif Aziz. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
#endregion

namespace CSharpMinifierConsole
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using CSharpMinifier;
    using Microsoft.Extensions.FileSystemGlobbing;
    using Mono.Options;
    using OptionSetArgumentParser = System.Func<System.Func<string, Mono.Options.OptionContext, bool>, string, Mono.Options.OptionContext, bool>;

    static partial class Program
    {
        static readonly Ref<bool> Verbose = Ref.Create(false);

        static int Wain(IEnumerable<string> args)
        {
            var help = Ref.Create(false);
            var globDir = Ref.Create((DirectoryInfo)null);

            var options = new OptionSet(CreateStrictOptionSetArgumentParser())
            {
                Options.Help(help),
                Options.Verbose(Verbose),
                Options.Debug,
                Options.Glob(globDir),
            };

            var tail = options.Parse(args);

            if (help)
            {
                Help("min", "[min]", options);
                return 0;
            }

            var command = tail.FirstOrDefault();
            var commandArgs = tail.Skip(1);
            var result = 0;

            switch (command)
            {
                case "min"   : Wain(commandArgs); break;
                case "help"  : HelpCommand(commandArgs); break;
                case "tokens": TokensCommand(commandArgs); break;
                case "hash"  : result = HashCommand(commandArgs); break;
                case "color":
                case "colour": ColorCommand(commandArgs); break;
                case "glob"  : GlobCommand(commandArgs); break;
                default      : DefaultCommand(); break;
            }

            return result;

            void DefaultCommand()
            {
                foreach (var (_, source) in ReadSources(tail, globDir))
                {
                    var nl = false;
                    foreach (var s in Minifier.Minify(source, null))
                    {
                        if (nl = s == null)
                            Console.WriteLine();
                        else
                            Console.Write(s);
                    }
                    if (!nl)
                        Console.WriteLine();
                }
            }
        }

        static void HelpCommand(IEnumerable<string> args)
        {
            switch (args.FirstOrDefault())
            {
                case null:
                case string command when command == "help":
                    Help("help", new Mono.Options.OptionSet());
                    break;
                case string command:
                    Wain(new [] { command, "--help" });
                        break;
            }
        }

        static IEnumerable<(string File, string Source)>
            ReadSources(IEnumerable<string> files, DirectoryInfo rootDir = null)
        {
            var stdin = Lazy.Create(() => Console.In.ReadToEnd());
            return ReadSources(files, rootDir, () => stdin.Value, File.ReadAllText);
        }

        static IEnumerable<(string File, T Source)>
            ReadSources<T>(IEnumerable<string> files,
                           DirectoryInfo rootDir,
                           Func<T> stdin, Func<string, T> reader)
        {
            if (rootDir != null)
            {
                var matcher = new Matcher();
                using (var e = files.GetEnumerator())
                {
                    if (!e.MoveNext())
                        yield return ("STDIN", stdin());

                    do
                    {
                        if (string.IsNullOrEmpty(e.Current))
                            continue;

                        if (e.Current[0] == '!')
                            matcher.AddExclude(e.Current.Substring(1));
                        else
                            matcher.AddInclude(e.Current);
                    }
                    while (e.MoveNext());
                }

                foreach (var r in matcher.GetResultsInFullPath(rootDir.FullName))
                    yield return (Path.GetRelativePath(rootDir.FullName, r), reader(r));
            }
            else
            {
                using (var e = files.GetEnumerator())
                {
                    if (!e.MoveNext())
                        yield return ("STDIN", stdin());

                    do
                    {
                        if (string.IsNullOrEmpty(e.Current))
                            continue;
                        yield return (e.Current, reader(e.Current));
                    }
                    while (e.MoveNext());
                }
            }
        }

        static class Options
        {
            public static Option Help(Ref<bool> value) =>
                new ActionOption("?|help|h", "prints out the options", _ => value.Value = true);

            public static Option Verbose(Ref<bool> value) =>
                new ActionOption("verbose|v", "enable additional output", _ => value.Value = true);

            public static readonly Option Debug =
                new ActionOption("d|debug", "debug break", vs => Debugger.Launch());

            public static Option Glob(Ref<DirectoryInfo> value) =>
                new ActionOption("glob=", "glob base directory", vs => value.Value = new DirectoryInfo(vs.Last()));
        }

        static OptionSetArgumentParser CreateStrictOptionSetArgumentParser()
        {
            var hasTailStarted = false;
            return (impl, arg, context) =>
            {
                if (hasTailStarted) // once a tail, always a tail
                    return false;

                var isOption = impl(arg, context);
                if (!isOption && !hasTailStarted)
                {
                    if (arg.Length > 1 && arg[0] == '-')
                        throw new Exception("Invalid argument: " + arg);

                    hasTailStarted = true;
                }

                return isOption;
            };
        }

        static int Main(string[] args)
        {
            try
            {
                return Wain(args);
            }
            catch (Exception e)
            {
                if (Verbose)
                    Console.Error.WriteLine(e);
                else
                    Console.Error.WriteLine(e.GetBaseException().Message);
                return 0xbad;
            }
        }
    }
}