﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AutoUpdate
{
    public partial class FrmMain : Form
    {
        string configPath, configName = "UpdateConfig.dat";
        string ftpUserName, ftpPassword;
        string webRootPath;
        JavaScriptSerializer convert = new JavaScriptSerializer();
        long currentIndex, currentCount;
        string currentFileName;
        long totalIndex, totalCount;
        public DateTime dtLastUpdateTime;

        public FrmMain()
        {
            InitializeComponent();
        }

        private void FrmMain_Shown(object sender, EventArgs e)
        {
            this.ftpUserName = ConfigurationManager.AppSettings["UserName"];
            this.ftpPassword = ConfigurationManager.AppSettings["Password"];
            this.configPath = ConfigurationManager.AppSettings["ConfigPath"];
            this.Download(this.configPath, configName, "", configName);
            var updateInfo = this.GetUpdateInfo();
            if (updateInfo == null)
            {
                MessageBoxEx.Show("获取更新配置失败!", "提示", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
                return;
            }
            this.webRootPath = updateInfo.RootPath;
            this.totalCount = updateInfo.FileList.Sum(s => s.Size);
            this.UpdateFileList(updateInfo.FileList);
        }

        public bool Download(string remotePath, string remoteFileName, string localPath, string localFileName)
        {
            FileStream outputStream = null;
            try
            {
                var localDir = AppDomain.CurrentDomain.BaseDirectory + localPath;
                if (!Directory.Exists(localDir))
                    Directory.CreateDirectory(localDir);
                outputStream = new FileStream(string.Format("{0}\\{1}", localDir, localFileName), FileMode.Create);
                FtpWebRequest reqFTP = (FtpWebRequest)WebRequest.Create(new Uri(string.Format("{0}//{1}", remotePath, remoteFileName)));
                reqFTP.Method = WebRequestMethods.Ftp.DownloadFile;
                reqFTP.UseBinary = true;
                reqFTP.Credentials = new NetworkCredential(ftpUserName, ftpPassword);
                FtpWebResponse response = (FtpWebResponse)reqFTP.GetResponse();
                Stream ftpStream = response.GetResponseStream();
                int bufferSize = 2048, readCount = 0;
                byte[] buffer = new byte[bufferSize];
                while (true)
                {
                    readCount = ftpStream.Read(buffer, 0, bufferSize);
                    if (readCount == 0)
                        break;
                    outputStream.Write(buffer, 0, readCount);
                    currentIndex += readCount;
                    totalIndex += readCount;
                }
                ftpStream.Close();
                response.Close();
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                outputStream.Close();
            }
        }

        public UpdateInfo GetUpdateInfo()
        {
            var configPath = AppDomain.CurrentDomain.BaseDirectory + configName;
            var info = convert.Deserialize<UpdateInfo>(File.ReadAllText(configPath));
            File.Delete(configPath);
            if (info == null)
                return null;
            var localPath = Application.StartupPath;
            var q = from file in Directory.GetFiles(localPath, "*.*", SearchOption.AllDirectories)
                    join fileNew in info.FileList
                    on new
                    {
                        Directory = Path.GetDirectoryName(file).Replace(localPath, ""),
                        Name = Path.GetFileName(file),
                        MD5 = GetMD5HashFromFile(file)
                    } equals new
                    {
                        fileNew.Directory,
                        fileNew.Name,
                        fileNew.MD5
                    }
                    select fileNew;
            info.FileList.RemoveAll(m => q.Contains(m));
            return info;
        }
        public void UpdateFileList(List<FileItem> fileItemList)
        {
            Task.Factory.StartNew(arg =>
            {
                var fileList = arg as List<FileItem>;
                if (fileList == null)
                    return;
                var result = true;
                foreach (var fileItem in fileList)
                {
                    currentIndex = 0;
                    currentCount = fileItem.Size;
                    currentFileName = fileItem.Name;
                    var fileResult = false;
                    if (fileItem.Name == "AutoUpdate.exe")
                        fileResult = this.Download(webRootPath + fileItem.Directory, fileItem.Name, fileItem.Directory, "AutoUpdate.exe.tmp");
                    else
                        fileResult = this.Download(webRootPath + fileItem.Directory, fileItem.Name, fileItem.Directory, fileItem.Name);
                    result = result && fileResult;
                }
                if (result)
                {
                    Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                    config.AppSettings.Settings["UpdateTime"].Value = this.dtLastUpdateTime.ToString("yyyy-MM-dd HH:mm:ss");
                    config.Save(ConfigurationSaveMode.Modified);
                }
                this.Invoke(new Action(() =>
                {
                    this.Close();
                }));
            }, fileItemList);
        }

        private void timerUpdate_Tick(object sender, EventArgs e)
        {
            this.labFileName.Text = this.currentFileName;
            this.labFileSize.Text = FormatFileSize(currentIndex) + "/" + FormatFileSize(currentCount);
            this.labFileSize.Left = this.Width - this.labFileSize.Width - 30;
            if (this.currentCount > 0)
            {
                this.pbCurrent.Value = this.currentIndex / (float)this.currentCount;
                this.pbCurrent.Invalidate();
            }
            if (this.totalCount > 0)
            {
                this.pbTotal.Value = this.totalIndex / (float)this.totalCount;
                this.pbTotal.Invalidate();
            }
        }

        public static string GetMD5HashFromFile(string fileName)
        {
            FileStream file;
            if (File.Exists(fileName))
                file = File.OpenRead(fileName);
            else
                return null;
            System.Security.Cryptography.MD5 md5 = new System.Security.Cryptography.MD5CryptoServiceProvider();
            byte[] retVal = md5.ComputeHash(file);
            file.Close();
            StringBuilder str = new StringBuilder();
            for (int i = 0; i < retVal.Length; i++)
            {
                str.Append(retVal[i].ToString("x2"));
            }
            return str.ToString().Trim().ToUpper();
        }

        internal static string FormatFileSize(long fileSize)
        {
            if (fileSize < 0)
                return "ErrorSize";
            else if (fileSize >= 1024 * 1024 * 1024)
                return string.Format("{0:########0.00} GB", ((Double)fileSize) / (1024 * 1024 * 1024));
            else if (fileSize >= 1024 * 1024)
                return string.Format("{0:####0.00} MB", ((Double)fileSize) / (1024 * 1024));
            else if (fileSize >= 1024)
                return string.Format("{0:####0.00} KB", ((Double)fileSize) / 1024);
            else
                return string.Format("{0} Bytes", fileSize);
        }
    }
}