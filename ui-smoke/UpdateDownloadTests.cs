using Fellowship;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

static class UpdateDownloadTests
{
    sealed class Samples : IProgress<(long Received,long? Total)>
    {
        public List<(long Received,long? Total)> Values=[];
        public void Report((long Received,long? Total) value)=>Values.Add(value);
    }
    public static async Task Run()
    {
        byte[] payload=Encoding.UTF8.GetBytes(new string('x',200000));
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        string url=$"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/package";
        var serve=Task.Run(async()=>
        {
            for(int i=0;i<2;i++)
            {
                using var client=await listener.AcceptTcpClientAsync();using var stream=client.GetStream();
                byte[] request=new byte[8192];await stream.ReadAsync(request);
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n"));await stream.WriteAsync(payload);
            }
        });
        try
        {
            string root=Path.GetFullPath("dist/update-download-tests");var sample=new Samples();
            var release=new AppUpdates.Release(new Version(99,0,0),url,Convert.ToHexString(SHA256.HashData(payload)),true);
            string file=await AppUpdates.Download(release,root,sample);
            if(!File.ReadAllBytes(file).SequenceEqual(payload)||sample.Values[^1]!=(payload.Length,(long?)payload.Length))throw new Exception("Download bytes/progress incorrect");
            bool rejected=false;try{await AppUpdates.Download(release with{Digest=new string('0',64)},root);}catch(InvalidDataException){rejected=true;}
            if(!rejected)throw new Exception("Bad checksum accepted");
            using var cancel=new CancellationTokenSource();cancel.Cancel();
            bool cancelled=false;try{await AppUpdates.Download(release,root,token:cancel.Token);}catch(OperationCanceledException){cancelled=true;}
            if(!cancelled)throw new Exception("Download cancellation ignored");
            await serve;Console.WriteLine("PASS streamed update progress, checksum rejection and cancellation");
        }
        finally{listener.Stop();}
    }
}
