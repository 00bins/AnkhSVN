// Copyright 2005-2009 The AnkhSVN Project
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.

using System;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Forms;
using Microsoft.VisualStudio.TextManager.Interop;
using Ankh.Scc;
using Ankh.Scc.UI;
using Ankh.UI;
using Ankh.UI.Annotate;
using Ankh.VS;
using SharpSvn;
using System.Collections.Generic;
using Ankh.UI.Commands;
using Microsoft.Win32;

namespace Ankh.Commands
{
    /// <summary>
    /// Command to identify which users to blame for which lines.
    /// </summary>
    [SvnCommand(AnkhCommand.ItemAnnotate)]
    [SvnCommand(AnkhCommand.LogAnnotateRevision)]
    [SvnCommand(AnkhCommand.SvnNodeAnnotate)]
    [SvnCommand(AnkhCommand.DocumentAnnotate)]
    class ItemAnnotateCommand : CommandBase
    {
        public override void OnUpdate(CommandUpdateEventArgs e)
        {
            switch (e.Command)
            {
                case AnkhCommand.SvnNodeAnnotate:
                    ISvnRepositoryItem ri = EnumTools.GetSingle(e.Selection.GetSelection<ISvnRepositoryItem>());
                    if (ri != null && ri.Origin != null && ri.NodeKind != SvnNodeKind.Directory)
                        return;
                    break;
                case AnkhCommand.ItemAnnotate:
                    foreach (SvnItem item in e.Selection.GetSelectedSvnItems(false))
                    {
                        if (item.IsFile && item.IsVersioned && item.HasCopyableHistory)
                            return;
                    }
                    break;
                case AnkhCommand.DocumentAnnotate:
                    if (e.Selection.ActiveDocumentSvnItem != null && e.Selection.ActiveDocumentSvnItem.HasCopyableHistory)
                        return;
                    break;
                case AnkhCommand.LogAnnotateRevision:
                    ILogControl logControl = e.Selection.GetActiveControl<ILogControl>();
                    if (logControl == null || logControl.Origins == null)
                    {
                        e.Visible = e.Enabled = false;
                        return;
                    }

                    if (!EnumTools.IsEmpty(e.Selection.GetSelection<ISvnLogChangedPathItem>()))
                        return;
                    break;
            }
            e.Enabled = false;
        }

        public override void OnExecute(CommandEventArgs e)
        {
            List<SvnOrigin> targets = new List<SvnOrigin>();
            SvnRevision startRev = SvnRevision.Zero;
            SvnRevision endRev = null;
            switch (e.Command)
            {
                case AnkhCommand.ItemAnnotate:
                    endRev = SvnRevision.Working;
                    foreach (SvnItem i in e.Selection.GetSelectedSvnItems(false))
                    {
                        if (i.IsFile && i.IsVersioned && i.HasCopyableHistory)
                            targets.Add(new SvnOrigin(i));
                    }
                    break;
                case AnkhCommand.LogAnnotateRevision:
                    foreach (ISvnLogChangedPathItem logItem in e.Selection.GetSelection<ISvnLogChangedPathItem>())
                    {
                        targets.Add(logItem.Origin);
                        endRev = logItem.Revision;
                    }
                    break;
                case AnkhCommand.SvnNodeAnnotate:
                    foreach (ISvnRepositoryItem item in e.Selection.GetSelection<ISvnRepositoryItem>())
                    {
                        targets.Add(item.Origin);
                        endRev = item.Revision;
                    }
                    break;
                case AnkhCommand.DocumentAnnotate:
                    //TryObtainBlock(e);
                    targets.Add(new SvnOrigin(e.GetService<ISvnStatusCache>()[e.Selection.ActiveDocumentFilename]));
                    endRev = SvnRevision.Working;
                    break;
            }

            if (targets.Count == 0)
                return;

            DoBlameExternal(targets);

            if (false)
            {
                bool ignoreEols = true;
                SvnIgnoreSpacing ignoreSpacing = SvnIgnoreSpacing.IgnoreSpace;
                bool retrieveMergeInfo = false;
                SvnOrigin target;

                if ((!e.DontPrompt && !Shift) || e.PromptUser)
                    using (AnnotateDialog dlg = new AnnotateDialog())
                    {
                        dlg.SetTargets(targets);
                        dlg.StartRevision = startRev;
                        dlg.EndRevision = endRev;

                        if (dlg.ShowDialog(e.Context) != DialogResult.OK)
                            return;

                        target = dlg.SelectedTarget;
                        startRev = dlg.StartRevision;
                        endRev = dlg.EndRevision;
                        ignoreEols = dlg.IgnoreEols;
                        ignoreSpacing = dlg.IgnoreSpacing;
                        retrieveMergeInfo = dlg.RetrieveMergeInfo;
                    }
                else
                {
                    SvnItem one = EnumTools.GetFirst(e.Selection.GetSelectedSvnItems(false));

                    if (one == null)
                        return;

                    target = new SvnOrigin(one);
                }

                if (startRev == SvnRevision.Working || endRev == SvnRevision.Working && target.Target is SvnPathTarget)
                {
                    IAnkhOpenDocumentTracker tracker = e.GetService<IAnkhOpenDocumentTracker>();
                    if (tracker != null)
                        tracker.SaveDocument(((SvnPathTarget)target.Target).FullPath);
                }

                DoBlame(e, target, startRev, endRev, ignoreEols, ignoreSpacing, retrieveMergeInfo);
            }

        }

        /*private void TryObtainBlock(CommandEventArgs e)
        {
            ISelectionContextEx ex = e.GetService<ISelectionContextEx>(typeof(ISelectionContext));

            if (ex == null)
                return;

            IVsTextView view = ex.ActiveDocumentFrameTextView;
            IVsTextLines lines;
            Guid languageService;
            IVsLanguageInfo info;

            if (view != null
                && VSErr.Succeeded(view.GetBuffer(out lines))
                && VSErr.Succeeded(lines.GetLanguageServiceID(out languageService))
                && null != (info = e.QueryService<IVsLanguageInfo>(languageService)))
            {
                GC.KeepAlive(info);
                IVsLanguageBlock b = info as IVsLanguageBlock;
                if (b != null)
                {
                    GC.KeepAlive(b);
                }
            }
            //IVsLanguageBlock


            GC.KeepAlive(ex);
        }*/

        /*
         * 
---------------------------
TortoiseBlame
---------------------------
TortoiseBlame should not be started directly! Use
TortoiseProc.exe /command:blame /path:"path\to\file"
instead.

TortoiseBlame.exe blamefile [logfile [viewtitle]] [/line:linenumber] [/path:originalpath] [/pegrev:peg] [/revrange:text] [/ignoreeol] [/ignorespaces] [/ignoreallspaces]
Key Name:          HKEY_LOCAL_MACHINE\SOFTWARE\Classes\CLSID\{30351346-7B7D-4FCC-81B4-1E394CA267EB}\InProcServer32
Class Name:        <NO CLASS>
Last Write Time:   05/02/2024 - 17:04
Value 0
  Name:            <NO NAME>
  Type:            REG_SZ
  Data:            C:\Program Files\TortoiseSVN\bin\TortoiseStub.dll
*/

        static void DoBlameExternal(List<SvnOrigin> targets)
        {
            object tsvn = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Classes\CLSID\{30351346-7B7D-4FCC-81B4-1E394CA267EB}\InProcServer32",null,null);

            if(object.Equals(tsvn, null))
            {
                MessageBox.Show(string.Format("Blame requires tortoise svn\nPlease ensure it is installed"), "Error",
                    System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
                return;
            }

            string tproc = tsvn.ToString().Replace("Stub.dll", "Proc.exe");

            if(string.Equals(tproc,tsvn.ToString()))
            {
                MessageBox.Show(string.Format("Blame can't deduce tortoisproc.exe path from registry\nPlease ensure TortoiseSVN is installed"), "Error",
                    System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
                return;
            }

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = tproc;
            startInfo.Arguments = string.Format("/command:blame /path:{0}",targets[0]);

            try
            {
                Process.Start(startInfo);

            }
            catch (System.Exception e)
            { 
              
                MessageBox.Show(string.Format("Blame requires tortoise svn\nPlease ensure {0} exists",startInfo.FileName), "Error",
                    System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
            
            }
        }

        static void DoBlame(CommandEventArgs e, SvnOrigin item, SvnRevision revisionStart, SvnRevision revisionEnd, bool ignoreEols, SvnIgnoreSpacing ignoreSpacing, bool retrieveMergeInfo)
        {
            SvnWriteArgs wa = new SvnWriteArgs();
            wa.Revision = revisionEnd;

            SvnBlameArgs ba = new SvnBlameArgs();
            ba.Start = revisionStart;
            ba.End = revisionEnd;
            ba.IgnoreLineEndings = ignoreEols;
            ba.IgnoreSpacing = ignoreSpacing;
            ba.RetrieveMergedRevisions = retrieveMergeInfo;

            SvnTarget target = item.Target;

            IAnkhTempFileManager tempMgr = e.GetService<IAnkhTempFileManager>();
            string tempFile = tempMgr.GetTempFileNamed(target.FileName);

            Collection<SvnBlameEventArgs> blameResult = null;

            bool retry = false;
            ProgressRunnerResult r = e.GetService<IProgressRunner>().RunModal(CommandStrings.Annotating, delegate(object sender, ProgressWorkerArgs ee)
            {
                using (FileStream fs = File.Create(tempFile))
                {
                    ee.Client.Write(target, fs, wa);
                }

                ba.SvnError +=
                    delegate(object errorSender, SvnErrorEventArgs errorEventArgs)
                    {
                        if (errorEventArgs.Exception is SvnClientBinaryFileException)
                        {
                            retry = true;
                            errorEventArgs.Cancel = true;
                        }
                    };
                ee.Client.GetBlame(target, ba, out blameResult);
            });

            if (retry)
            {
                using (AnkhMessageBox mb = new AnkhMessageBox(e.Context))
                {
                    if (DialogResult.Yes != mb.Show(
                                                CommandStrings.AnnotateBinaryFileContinueAnywayText,
                                                CommandStrings.AnnotateBinaryFileContinueAnywayTitle,
                                                MessageBoxButtons.YesNo, MessageBoxIcon.Information))
                        return;

                    r = e.GetService<IProgressRunner>()
                            .RunModal(CommandStrings.Annotating,
                                      delegate(object sender, ProgressWorkerArgs ee)
                                      {
                                          ba.IgnoreMimeType = true;
                                          ee.Client.GetBlame(target, ba, out blameResult);
                                      });
                }
            }

            if (!r.Succeeded)
                return;

            #region 
            #endregion
            AnnotateEditorControl annEditor = null;
            IAnkhEditorResolver er = null;

            WithDPIAwareness.Run(AnkhDpiAwareness.SystemAware, () =>
            {
                annEditor = new AnnotateEditorControl();
                er = e.GetService<IAnkhEditorResolver>();

                annEditor.Create(e.Context, tempFile);
                annEditor.LoadFile(tempFile);
                annEditor.AddLines(item, blameResult);
            });

            // Detect and set the language service
            Guid language;
            if (er.TryGetLanguageService(Path.GetExtension(target.FileName), out language))
            {
                // Extension is mapped -> user
                annEditor.SetLanguageService(language);
            }
            else if (blameResult != null && blameResult.Count > 0 && blameResult[0].Line != null)
            {
                // Extension is not mapped -> Check if this is xml (like project files)
                string line = blameResult[0].Line.Trim();

                if (line.StartsWith("<?xml")
                    || (line.StartsWith("<") && line.Contains("xmlns=\"http://schemas.microsoft.com/developer/msbuild/")))
                {
                    if (er.TryGetLanguageService(".xml", out language))
                    {
                        annEditor.SetLanguageService(language);
                    }
                }
            }
        }
    }
}
