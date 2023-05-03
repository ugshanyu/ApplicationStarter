using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Xml.Linq;
using IWshRuntimeLibrary;
using Microsoft.Win32;
using System.Text;
using System.Xml;
using Newtonsoft.Json;
using SharpCompress.Archives.Rar;
using SharpCompress.Common;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Net.Http;

using SharpCompress.Archives;
using SharpCompress.Readers;
using System.IO;
using System.Management;
using System.Security.Cryptography;

async Task Main(string[] args)
{
    AddToStartup();
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    //string baseUrl = "http://localhost:8081";
    string baseUrl = "http://202.180.218.84/";
    Console.WriteLine("Хандах хаяг: " + baseUrl);
    string appFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AppFolder");
    try {
        String uniqueId;
        using (RegistryKey key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Cryptography"))
        {
            uniqueId = GetUniqueDeviceId();
        }
        Console.WriteLine("ID: " + uniqueId);
        string manifestFilePath = FindApplicationFile(appFolderPath);
        string clickOnceApplicationVersion = GetClickOnceApplicationVersion(manifestFilePath);
        var client = new HttpClient();
        string token = await GetTokenTo(client, baseUrl, uniqueId);
        bool shouldUpdate = await ShouldUpdateAsync(client, baseUrl, clickOnceApplicationVersion, uniqueId);

        //string latestVersion = await GetLatestVersionAsync(client, baseUrl);

        if (shouldUpdate)
        {
            string zipFilePath = await DownloadAppAsync(client, baseUrl);
            if (zipFilePath != null)
            {
                DecompressApp(zipFilePath, appFolderPath);
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("Error" + ex.ToString());
    }
    
    RunApplicationManifest(appFolderPath);
}

async Task<String> GetTokenTo(HttpClient client, String baseUrl, String uniqueId)
{
    String responseString = "";
    var waitTime = 0;

    var obj = new
    {
        code = uniqueId
    };

    var json = JsonConvert.SerializeObject(obj);
    var content = new StringContent(json, Encoding.UTF8, "application/json");
    var response = await client.PostAsync($"{baseUrl}/manage-api/v1/recordApi/login", content);
    //if response is success then get token if not then log error and try again after 1 plus minute from last try
    if (response.IsSuccessStatusCode)
    {
        responseString = await response.Content.ReadAsStringAsync();
        Console.WriteLine("Сервэртэй амжилтай холбогдож токен авлаа");
    }
    else
    {
        waitTime = waitTime + 1;
        await Task.Delay(waitTime * 60000);
        //System.Threading.Thread.Sleep(waitTime * 60000);
        await GetTokenTo(client, baseUrl, uniqueId);

    }
    client.DefaultRequestHeaders.Add("x-auth-token", responseString);
    return responseString;
    
}

static async Task<string> GetLatestVersionAsync(HttpClient client, string baseUrl)
{
    try
    {
        string url = baseUrl + "/manage-api/v1/version/name";
        HttpResponseMessage response = await client.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            string version = await response.Content.ReadAsStringAsync();
            return version;
        }
        else
        {
            Console.WriteLine($"Error: {response.StatusCode}");
            return null;
        }
    }
    catch (Exception e)
    {
        Console.WriteLine($"Error: {e.Message}");
        return null;
    }
}


static async Task<bool> ShouldUpdateAsync(HttpClient client, string baseUrl, string version, string deviceCode)
{
    try
    {
        string url = baseUrl + "/manage-api/v1/version/should-update";
        string urlWithVersion = $"{url}?version={version}&deviceCode={deviceCode}";

        HttpResponseMessage response = await client.GetAsync(urlWithVersion);

        if (response.IsSuccessStatusCode)
        {
            string shouldUpdate = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Одоогийн хувилбар: {version}");
            Console.WriteLine($"Шинэ хувилбар татах эсэх: {shouldUpdate != "false"}");
            return shouldUpdate != "false";
        }
        else
        {
            Console.WriteLine($"Алдаа ShouldUpdateAsync: {response.StatusCode}");
            return true;
        }
    }
    catch (Exception e)
    {
        Console.WriteLine($"Алдаа ShouldUpdateAsync: {e.Message}");
        return false;
    }
}

static async Task<bool> CheckUpdate(HttpClient client, string url, string currentVersion)
{
    using var response = await client.GetAsync(url);
    
    
    //string zipFilePath = Path.Combine(Path.GetTempPath(), "app.zip");
    //return string.Compare(currentVersion, latestVersion) < 0;
    return true;
}

static string GetUniqueDeviceId()
{
    StringBuilder sb = new StringBuilder();

    try
    {
        // Get processor ID
        using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor"))
        {
            foreach (ManagementObject queryObj in searcher.Get())
            {
                sb.Append(queryObj["ProcessorId"]);
                break;
            }
        }

        // Get motherboard serial number
        using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard"))
        {
            foreach (ManagementObject queryObj in searcher.Get())
            {
                sb.Append(queryObj["SerialNumber"]);
                break;
            }
        }

        // Get primary disk serial number
        using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive"))
        {
            foreach (ManagementObject queryObj in searcher.Get())
            {
                sb.Append(queryObj["SerialNumber"]);
                break;
            }
        }

        // Hash the combined string to create a unique identifier
        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("Error: " + ex.Message);
        return string.Empty;
    }
}


static async Task<string> DownloadAppAsync(HttpClient clientt, string baseUrl)
{
    string url = baseUrl + "/manage-api/v1/version/download_latest";
    string zipFilePath = Path.Combine(Path.GetTempPath(), "app.zip");

    using (var client = new HttpClientDownloadWithProgress(url, zipFilePath))
    {
        client.ProgressChanged += (totalFileSize, totalBytesDownloaded, progressPercentage) => {
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write(new string(' ', Console.BufferWidth)); // Clear the current line
            Console.SetCursorPosition(0, Console.CursorTop); // Reset the cursor position

            if (totalFileSize.HasValue)
            {
                //Console.Write($"{progressPercentage}%");
                Console.Write($"{progressPercentage}% ({totalBytesDownloaded}/{totalFileSize})");
            }
            else
            {
                Console.Write($"Татаж байна... {totalBytesDownloaded} bytes");
            }
        };

        await client.StartDownload();
        Console.WriteLine("\nТаталт дууслаа."); // Print download complete message
    }
    return zipFilePath;
}

static async Task<string> DDownloadAppAsync(HttpClient client, string baseUrl)
{
    string url = baseUrl + "/manage-api/v1/version/download_latest";
    string zipFilePath = Path.Combine(Path.GetTempPath(), "app.zip");

    CancellationTokenSource cts = new CancellationTokenSource();
    Task rotatingIndicatorTask = ShowRotatingIndicator(cts.Token);

    using var response = await client.GetAsync(url);

    // Cancel the rotating indicator task and wait for it to complete
    cts.Cancel();
    await rotatingIndicatorTask;

    if (response.IsSuccessStatusCode)
    {
        using var fileStream = new FileStream(zipFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await response.Content.CopyToAsync(fileStream);
    }
    else
    {
        Console.WriteLine($"Алдаа DownloadAppAsync: {response.StatusCode}");
        return null;
    }
    return zipFilePath;
}

static async Task ShowRotatingIndicator(CancellationToken cancellationToken)
{
    char[] sequence = new char[] { '|', '/', '-', '\\' };
    int counter = 0;

    Console.Write("Downloading ");

    while (!cancellationToken.IsCancellationRequested)
    {
        Console.Write(sequence[counter]);
        counter = (counter + 1) % sequence.Length;
        await Task.Delay(100);
        Console.SetCursorPosition(Console.CursorLeft - 1, Console.CursorTop);
    }

    Console.WriteLine();
}



//static void ReportProgress(HttpProgress progress)
//{
//    if (progress.TotalBytesToReceive.HasValue)
//    {
//        Console.WriteLine($"Download progress: {progress.BytesReceived * 100 / progress.TotalBytesToReceive.Value}%");
//    }
//}


static void GrantWriteAccess(string directoryPath, string userOrGroupName)
{
    // Get the DirectoryInfo object for the directory.
    DirectoryInfo directoryInfo = new DirectoryInfo(directoryPath);

    // Get the current access control settings for the directory.
    DirectorySecurity directorySecurity = directoryInfo.GetAccessControl();

    // Add the FileSystemAccessRule to grant write access to the specified user or group.
    FileSystemAccessRule fileSystemAccessRule = new FileSystemAccessRule(userOrGroupName, FileSystemRights.Write, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow);
    directorySecurity.AddAccessRule(fileSystemAccessRule);

    // Apply the new access control settings to the directory.
    directoryInfo.SetAccessControl(directorySecurity);
}

static bool ShouldDownload(string latestVersion, string clickOnceApplicationVersion)
{
    return !string.Equals(latestVersion, clickOnceApplicationVersion);
}

static string GetClickOnceApplicationVersion(string manifestFilePath)
{
    if (!System.IO.File.Exists(manifestFilePath))
    {
        Console.WriteLine($"Manifest file not found: {manifestFilePath}");
        return null;
    }

    XDocument manifest = XDocument.Load(manifestFilePath);
    XNamespace asmv1 = "urn:schemas-microsoft-com:asm.v1";
    XElement assemblyIdentity = manifest.Root.Element(asmv1 + "assemblyIdentity");

    if (assemblyIdentity != null)
    {
        XAttribute versionAttribute = assemblyIdentity.Attribute("version");
        if (versionAttribute != null)
        {
            return versionAttribute.Value;
        }
    }

    return null;
}

//static void DecompressApp(string rarFilePath, string appFolderPath)
//{
//    if (Directory.Exists(appFolderPath))
//    {
//        Directory.Delete(appFolderPath, true);
//    }

//    using (var archive = RarArchive.Open(rarFilePath))
//    {
//        foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
//        {
//            string destinationPath = Path.Combine(appFolderPath, entry.Key);
//            string destinationDirectory = Path.GetDirectoryName(destinationPath);

//            if (!Directory.Exists(destinationDirectory))
//            {
//                Directory.CreateDirectory(destinationDirectory);
//                GrantWriteAccess(destinationDirectory, "Users"); // You can replace "Users" with the desired user or group name.
//            }

//            entry.WriteToFile(destinationPath, new ExtractionOptions
//            {
//                ExtractFullPath = true,
//                Overwrite = true
//            });
//        }
//    }
//}

static bool IsValidZipFile(string filePath)
{
    using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
    {
        byte[] fileSignature = new byte[4];
        fileStream.Read(fileSignature, 0, 4);
        return fileSignature[0] == 0x50 && fileSignature[1] == 0x4B && fileSignature[2] == 0x03 && fileSignature[3] == 0x04;
    }
}

static void DecompressApp(string zipFilePath, string appFolderPath)
{
    bool isValid = IsValidZipFile(zipFilePath);
    if (Directory.Exists(appFolderPath))
    {
        Directory.Delete(appFolderPath, true);
    }

    // Create the appFolderPath if it doesn't exist
    Directory.CreateDirectory(appFolderPath);

    // Extract the contents of the zip file to the appFolderPath
    ZipFile.ExtractToDirectory(zipFilePath, appFolderPath);
    Console.WriteLine("Шинэ хувилбарыг амжилттай татаж задаллаа");
}

//static void RunApplicationManifest(string manifestFilePath)
//{
//    if (!System.IO.File.Exists(manifestFilePath))
//    {
//        Console.WriteLine($"Manifest file not found: {manifestFilePath}");
//        return;
//    }

//    // Here, you can add code to process the application manifest file as needed
//}


//static void RunApplicationManifest(string appFolderPath)
//{
//    string manifestFilePath = FindApplicationFile(appFolderPath);

//    if (manifestFilePath == null)
//    {
//        Console.WriteLine($"Manifest file not found in {appFolderPath}");
//        return;
//    }

//    string dfshimPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "dfshim.dll");

//    if (!System.IO.File.Exists(dfshimPath))
//    {
//        Console.WriteLine($"dfshim.dll not found in {Environment.SpecialFolder.System}");
//        return;
//    }

//    ProcessStartInfo startInfo = new ProcessStartInfo("rundll32.exe");
//    startInfo.Arguments = $"\"{dfshimPath}\",ShOpenVerbApplication \"{manifestFilePath}\"";
//    startInfo.WorkingDirectory = appFolderPath;
//    Process.Start(startInfo);
//}

static void RunApplicationManifest(string appFolderPath)
{
    string manifestFilePath = FindApplicationFile(appFolderPath);

    if (manifestFilePath == null)
    {
        Console.WriteLine($"Manifest file not found in {appFolderPath}");
        return;
    }

    try
    {
        string tempBatchFilePath = Path.Combine(Path.GetTempPath(), "launch_clickonce_app.bat");
        System.IO.File.WriteAllText(tempBatchFilePath, $"start \"\" \"{manifestFilePath}\"{Environment.NewLine}");

        ProcessStartInfo startInfo = new ProcessStartInfo(tempBatchFilePath);
        startInfo.WorkingDirectory = appFolderPath;
        startInfo.WindowStyle = ProcessWindowStyle.Hidden;
        Process process = Process.Start(startInfo);
        process.WaitForExit();

        System.IO.File.Delete(tempBatchFilePath);
        //write me a method that prints current weather
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
    Console.ReadLine();
}

static string FindApplicationFile(string appFolderPath)
{
    if (!Directory.Exists(appFolderPath))
    {
        return null;
    }

    var searchPaths = new List<string>
    {
        appFolderPath,
    };

    foreach (string path in searchPaths)
    {
        var manifestFiles = Directory.GetFiles(path, "*.application");

        if (manifestFiles.Length > 0)
        {
            return manifestFiles[0]; // Return the first manifest file found
        }
    }

    return null;
}

static string GetAppExecutablePathFromManifest(string manifestFilePath)
{
    var manifest = XDocument.Load(manifestFilePath);
    var appElement = manifest.Element("Application");
    var executablePath = appElement?.Element("Executable")?.Value;

    return Path.Combine(Path.GetDirectoryName(manifestFilePath), executablePath);
}

static void OpenApplication(string appExecutablePath)
{
    if (!System.IO.File.Exists(appExecutablePath))
    {
        Console.WriteLine($"Executable file not found: {appExecutablePath}");
        return;
    }

    var processStartInfo = new ProcessStartInfo(appExecutablePath);
    Process.Start(processStartInfo);
}

static void AddToStartup()
{
    string appDirectory = AppContext.BaseDirectory;
    string appName = Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetExecutingAssembly().Location);
    string appPath = Path.Combine(appDirectory, $"{appName}.exe");

    RegistryKey reg = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
    reg.SetValue(appName, appPath);
}



// Add all the other methods as provided before, but without the "private" access modifier

await Main(args);


public class HttpClientDownloadWithProgress : IDisposable
{
    private readonly string _downloadUrl;
    private readonly string _destinationFilePath;

    private HttpClient _httpClient;

    public delegate void ProgressChangedHandler(long? totalFileSize, long totalBytesDownloaded, double? progressPercentage);

    public event ProgressChangedHandler ProgressChanged;

    public HttpClientDownloadWithProgress(string downloadUrl, string destinationFilePath)
    {
        _downloadUrl = downloadUrl;
        _destinationFilePath = destinationFilePath;
    }

    public async Task StartDownload()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromDays(1) };

        using (var response = await _httpClient.GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            await DownloadFileFromHttpResponseMessage(response);
    }

    private async Task DownloadFileFromHttpResponseMessage(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        //Console.WriteLine($"Total bytes: {totalBytes}");

        using (var contentStream = await response.Content.ReadAsStreamAsync())
            await ProcessContentStream(totalBytes, contentStream);
    }

    private async Task ProcessContentStream(long? totalDownloadSize, Stream contentStream)
    {
        var totalBytesRead = 0L;
        var readCount = 0L;
        var buffer = new byte[8192];
        var isMoreToRead = true;

        using (var fileStream = new FileStream(_destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
        {
            do
            {
                var bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    isMoreToRead = false;
                    TriggerProgressChanged(totalDownloadSize, totalBytesRead);
                    continue;
                }

                await fileStream.WriteAsync(buffer, 0, bytesRead);

                totalBytesRead += bytesRead;
                readCount += 1;

                if (readCount % 100 == 0)
                    TriggerProgressChanged(totalDownloadSize, totalBytesRead);
            }
            while (isMoreToRead);
        }
    }

    private void TriggerProgressChanged(long? totalDownloadSize, long totalBytesRead)
    {
        if (ProgressChanged == null)
            return;

        double? progressPercentage = null;
        if (totalDownloadSize.HasValue)

            progressPercentage = Math.Round((double)totalBytesRead / totalDownloadSize.Value * 100, 2);

        ProgressChanged(totalDownloadSize, totalBytesRead, progressPercentage);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}