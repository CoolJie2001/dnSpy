﻿/*
    Copyright (C) 2014-2015 de4dot@gmail.com

    This file is part of dnSpy

    dnSpy is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    dnSpy is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with dnSpy.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using dnSpy.Contracts.Files.TreeView.Resources;
using dnSpy.Shared.UI.MVVM.Dialogs;
using dnSpy.Shared.UI.Properties;
using WF = System.Windows.Forms;

namespace dnSpy.Shared.UI.Files.TreeView.Resources {
	public static class SaveResources {
		static readonly HashSet<char> invalidFileNameChar = new HashSet<char>();
		static SaveResources() {
			foreach (var c in Path.GetInvalidFileNameChars())
				invalidFileNameChar.Add(c);
			foreach (var c in Path.GetInvalidPathChars())
				invalidFileNameChar.Add(c);
		}

		public static ResourceData[] GetResourceData(IResourceDataProvider[] nodes, ResourceDataType resourceDataType) {
			if (nodes == null)
				return new ResourceData[0];
			return nodes.SelectMany(a => a.GetResourceData(resourceDataType)).ToArray();
		}

		public static void Save(IResourceDataProvider[] nodes, bool useSubDirs, ResourceDataType resourceDataType, Window ownerWindow = null) {
			if (nodes == null)
				return;

			Tuple<ResourceData, string>[] files;
			try {
				files = GetFiles(GetResourceData(nodes, resourceDataType), useSubDirs).ToArray();
			}
			catch (Exception ex) {
				App.MsgBox.Instance.Show(ex);
				return;
			}
			if (files.Length == 0)
				return;

			var data = new ProgressVM(Dispatcher.CurrentDispatcher, new ResourceSaver(files));
			var win = new ProgressDlg();
			win.DataContext = data;
			win.Owner = ownerWindow ?? Contracts.DnSpy.App.AppWindow.MainWindow;
			win.Title = files.Length == 1 ? dnSpy_Shared_UI_Resources.SaveResource : dnSpy_Shared_UI_Resources.SaveResources;
			var res = win.ShowDialog();
			if (res != true)
				return;
			if (!data.WasError)
				return;
			App.MsgBox.Instance.Show(string.Format(dnSpy_Shared_UI_Resources.AnErrorOccurred, data.ErrorMessage));
		}

		static IEnumerable<Tuple<ResourceData, string>> GetFiles(ResourceData[] infos, bool useSubDirs) {
			if (infos.Length == 1) {
				var info = infos[0];
				var name = FixFileNamePart(GetFileName(info.Name));
				var dlg = new WF.SaveFileDialog {
					RestoreDirectory = true,
					ValidateNames = true,
					FileName = name,
				};
				var ext = Path.GetExtension(name);
				dlg.DefaultExt = string.IsNullOrEmpty(ext) ? string.Empty : ext.Substring(1);
				if (dlg.ShowDialog() != WF.DialogResult.OK)
					yield break;
				yield return Tuple.Create(info, dlg.FileName);
			}
			else {
				var dlg = new WF.FolderBrowserDialog();
				if (dlg.ShowDialog() != WF.DialogResult.OK)
					yield break;
				string baseDir = dlg.SelectedPath;
				foreach (var info in infos) {
					var name = GetCleanedPath(info.Name, useSubDirs);
					var pathName = Path.Combine(baseDir, name);
					yield return Tuple.Create(info, pathName);
				}
			}
		}

		static string GetFileName(string s) {
			int index = Math.Max(s.LastIndexOf('/'), s.LastIndexOf('\\'));
			if (index < 0)
				return s;
			return s.Substring(index + 1);
		}

		static string FixFileNamePart(string s) {
			var sb = new StringBuilder(s.Length);

			foreach (var c in s) {
				if (invalidFileNameChar.Contains(c))
					sb.Append('_');
				else
					sb.Append(c);
			}

			return sb.ToString();
		}

		static string GetCleanedPath(string s, bool useSubDirs) {
			if (!useSubDirs)
				return FixFileNamePart(GetFileName(s));

			string res = string.Empty;
			foreach (var part in s.Replace('/', '\\').Split('\\'))
				res = Path.Combine(res, FixFileNamePart(part));
			return res;
		}
	}

	sealed class ResourceSaver : IProgressTask {
		public bool IsIndeterminate {
			get { return false; }
		}

		public double ProgressMinimum {
			get { return 0; }
		}

		public double ProgressMaximum {
			get { return fileInfos.Length; }
		}

		readonly Tuple<ResourceData, string>[] fileInfos;

		public ResourceSaver(Tuple<ResourceData, string>[] files) {
			this.fileInfos = files;
		}

		public void Execute(IProgress progress) {
			var buf = new byte[64 * 1024];
			for (int i = 0; i < fileInfos.Length; i++) {
				progress.ThrowIfCancellationRequested();
				var info = fileInfos[i];
				progress.SetDescription(info.Item2);
				progress.SetTotalProgress(i);
				Directory.CreateDirectory(Path.GetDirectoryName(info.Item2));
				var file = File.Create(info.Item2);
				try {
					var stream = info.Item1.GetStream(progress.Token);
					stream.Position = 0;
					for (;;) {
						int len = stream.Read(buf, 0, buf.Length);
						if (len <= 0) {
							if (stream.Position != stream.Length)
								throw new Exception("Could not read all bytes");
							break;
						}
						file.Write(buf, 0, len);
					}
				}
				catch {
					file.Dispose();
					try { File.Delete(info.Item2); }
					catch { }
					throw;
				}
				finally {
					file.Dispose();
				}
			}
			progress.SetTotalProgress(fileInfos.Length);
		}
	}
}