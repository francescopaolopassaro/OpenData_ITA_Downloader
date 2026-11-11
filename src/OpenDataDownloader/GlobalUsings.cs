// Shared using directives available across all files in this project.
global using System;
global using System.Collections.Concurrent;
global using System.ComponentModel.DataAnnotations;
global using System.Diagnostics;
global using System.IO;
global using System.Linq;
global using System.Net.Http;
global using System.Net.Http.Headers;
global using System.Net;
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using System.Threading;
global using System.Threading.Tasks;
global using WebUtility = System.Net.WebUtility;
global using FileInfo = System.IO.FileInfo;
global using File = System.IO.File;
global using Path = System.IO.Path;
global using Directory = System.IO.Directory;
global using DirectoryInfo = System.IO.DirectoryInfo;
global using Stopwatch = System.Diagnostics.Stopwatch;
global using ILogger = Microsoft.Extensions.Logging.ILogger;
global using AsyncPolicy = Polly.AsyncPolicy;

// Project-internal namespaces
global using OpenDataDownloader.Contracts;
global using OpenDataDownloader.Core;
