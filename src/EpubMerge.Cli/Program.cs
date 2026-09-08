using System.Text;
using EpubMerge.Cli;

Console.OutputEncoding = Encoding.UTF8;
return await CliRunner.RunAsync(args);
