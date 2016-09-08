using System;
using System.Collections.Specialized;
using System.Text;
using System.Web;
using System.Net;
using System.IO;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace hashtopus_hcdownloader
{
    class hcdownloader
    {
        static void Main(string[] args)
        {
            // set vars
            string domain = "http://hashcat.net";
            string tmpfile = "hashcat.7z";
            string url = "";
            string pwd = "";

            if (args.Length >= 1)
            {
                url = args[0];
            }

            if (args.Length >= 2)
            {
                pwd = args[1];
            }

            // check 7zip presence
            if (Run7z("", true) == false)
            {
                Console.WriteLine("7z is not in PATH or current directory.");
                return;
            }

            // get web data
            WebClient wcli = new WebClient();
            Console.WriteLine("Downloading website contents...");
            string site = wcli.DownloadString(domain + "/hashcat/");

            // search for download link
            Regex cosi = new Regex(@"\/files\/hashcat-[0-9.]*.*\.7z");
            Match mac = cosi.Match(site);
            string soubor = mac.Value;

            // download the archive
            if (File.Exists(tmpfile)) File.Delete(tmpfile);
            Console.WriteLine("Downloading 7z archive...");
            wcli.DownloadFile(domain + soubor, "hashcat.7z");
            cosi = new Regex("hashcat-[0-9]*.[0-9]*");
            string rootdir = cosi.Match(soubor).Value;

            string[] soubory = new string[] { Path.Combine("OpenCL", "*"), "hashcat.hcstat", "hashcat.hctune", "hashcat32.bin", "hashcat32.exe", "hashcat64.bin", "hashcat64.exe" };
            for (int i = 0; i < soubory.Length; i++)
            {
                soubory[i] = Path.Combine(rootdir, soubory[i]);
            }

            // unpack only the required files
            Console.WriteLine("Extracting from 7z...");
            Run7z("x hashcat.7z " + string.Join(" ", soubory));

            // delete the archive once unpacked
            File.Delete("hashcat.7z");

            if (Directory.Exists("hashcat")) Directory.Delete("hashcat");
            Directory.Move(rootdir, "hashcat");
            string vysledek = rootdir + ".zip";

            if (File.Exists(vysledek)) File.Delete(vysledek);

            // pack the archive to zip
            Console.WriteLine("Repacking to zip...");
            Run7z("a -r -tZip -mx=9 " + vysledek + " " + Path.Combine("hashcat", "*"));

            // delete the temp dir
            Directory.Delete("hashcat", true);

            Console.WriteLine("File " + vysledek + " created.");

            if (url == "")
            {
                Console.Write("Do you want to upload the file to your Hashtopus (y/n)? ");
                char cont = Console.ReadKey().KeyChar;
                Console.WriteLine();
                if (cont != 'Y' && cont != 'y')
                {
                    return;
                }
            }
            if (url == "")
            {
                Console.Write("Enter your admin.php URL: ");
                url = Console.ReadLine();
            }
            string obsah = wcli.DownloadString(url);
            if (!obsah.Contains("<title>Hashtopus"))
            {
                Console.WriteLine("Hashtopus admin not found on this URL.");
                return;
            }

            if (pwd == "")
            {
                Console.Write("Enter admin password: ");
                pwd = Console.ReadLine();
            }
            wcli.Headers.Add("Content-Type", "application/x-www-form-urlencoded");
            NameValueCollection values = new NameValueCollection();
            values.Add("pwd", pwd);

            Console.WriteLine("Logging in...");
            byte[] result = wcli.UploadValues(url, "POST", values);
            obsah = Encoding.UTF8.GetString(result);

            string kuk = "";
            if (obsah.Contains(" name=\"pwd\""))
            {
                Console.WriteLine("Wrong password.");
                return;
            }
            else
            {
                kuk = wcli.ResponseHeaders["Set-Cookie"];
                kuk = kuk.Substring(0, kuk.IndexOf("; "));
                wcli.Headers.Add("Cookie", kuk);
            }

            Console.WriteLine("Checking global files...");
            obsah = wcli.DownloadString(url + "?a=files");
            if (obsah.Contains(">" + vysledek + "</a>"))
            {
                Console.WriteLine("File already exists in your Hashtopus system.");
                return;
            }

            Console.WriteLine("Uploading hashcat...");
            WebRequest wr = WebRequest.Create(url + "?a=filesp");
            wr.Method = "POST";
            string bound = "---------------------------hashcat";
            wr.ContentType = "multipart/form-data; boundary=" + bound;
            wr.Headers.Add("Cookie", kuk);
            bound = "--" + bound;

            Stream rs = wr.GetRequestStream();

            byte[] buf;
            buf = Encoding.ASCII.GetBytes(string.Format("{0}{1}Content-Disposition: form-data; name=\"source\"{1}{1}upload{1}", bound, Environment.NewLine));
            rs.Write(buf, 0, buf.Length);
            buf = Encoding.ASCII.GetBytes(string.Format("{0}{1}Content-Disposition: form-data; name=\"upfile[]\"; filename=\"{2}\"{1}Content-Type: application/octet-stream{1}{1}", bound, Environment.NewLine, vysledek));
            rs.Write(buf, 0, buf.Length);

            Console.WriteLine("Reading zip file...");
            FileStream fs = File.Open(vysledek, FileMode.Open);
            byte[] fbuf = new byte[4096];
            int count = 0;
            while ((count = fs.Read(fbuf, 0, fbuf.Length)) != 0)
                rs.Write(fbuf, 0, count);
            fs.Close();

            buf = Encoding.ASCII.GetBytes(string.Format("{1}{0}--", bound, Environment.NewLine));
            rs.Write(buf, 0, buf.Length);
            rs.Close();

            WebResponse wre = wr.GetResponse();
            StreamReader sr = new StreamReader(wre.GetResponseStream());
            obsah = sr.ReadToEnd();

            if (!obsah.Contains("OK (<a href=\"?a=files#"))
            {
                Console.WriteLine("Could not upload file, please upload manually");
                return;
            }

            string id = obsah.Substring(obsah.IndexOf("?a=files#") + 9);
            id = id.Substring(0, id.IndexOf("\""));

            Console.WriteLine("Creating new release...");
            values = new NameValueCollection();
            values.Add("file", id);
            values.Add("version", rootdir.Substring(rootdir.IndexOf("-") + 1));
            result = wcli.UploadValues(url + "?a=newreleasep", "POST", values);
            obsah = Encoding.UTF8.GetString(result);

            if (obsah.Contains("OK<br>"))
            {
                Console.WriteLine("All done.");
            } else { 
                Console.WriteLine("Could not create release, please create manually.");
            }

        }


        static bool Run7z(string parameter, bool quiet = false)
        {
            Process proc = new Process();
            ProcessStartInfo sinfo = new ProcessStartInfo("7z", parameter);
            sinfo.UseShellExecute = false;
            if (quiet)
            {
                sinfo.RedirectStandardOutput = true;
            }
            proc.StartInfo = sinfo;
            try
            {
                proc.Start();
                proc.WaitForExit();
            }

            catch
            {
                return false;
            }

            return true;
        }


    }
}
