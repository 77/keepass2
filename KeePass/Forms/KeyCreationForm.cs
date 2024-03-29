/*
  KeePass Password Safe - The Open-Source Password Manager
  Copyright (C) 2003-2021 Dominik Reichl <dominik.reichl@t-online.de>

  This program is free software; you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation; either version 2 of the License, or
  (at your option) any later version.

  This program is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with this program; if not, write to the Free Software
  Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
*/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

using KeePass.App;
using KeePass.Resources;
using KeePass.UI;
using KeePass.Util;

using KeePassLib;
using KeePassLib.Cryptography;
using KeePassLib.Keys;
using KeePassLib.Native;
using KeePassLib.Resources;
using KeePassLib.Security;
using KeePassLib.Serialization;
using KeePassLib.Utility;

namespace KeePass.Forms
{
	public partial class KeyCreationForm : Form
	{
		private CompositeKey m_pKey = null;
		private bool m_bCreatingNew = false;
		private IOConnectionInfo m_ioInfo = new IOConnectionInfo();

		private PwInputControlGroup m_icgPassword = new PwInputControlGroup();
		private Image m_imgKeyFileWarning = null;
		private Image m_imgAccWarning = null;
		// private uint m_uBlockUpdate = 0;

		public CompositeKey CompositeKey
		{
			get
			{
				Debug.Assert(m_pKey != null);
				return m_pKey;
			}
		}

		public KeyCreationForm()
		{
			InitializeComponent();

			SecureTextBoxEx.InitEx(ref m_tbPassword);
			SecureTextBoxEx.InitEx(ref m_tbRepeatPassword);

			GlobalWindowManager.InitializeForm(this);
		}

		public void InitEx(IOConnectionInfo ioInfo, bool bCreatingNew)
		{
			if(ioInfo != null) m_ioInfo = ioInfo;

			m_bCreatingNew = bCreatingNew;
		}

		private void OnFormLoad(object sender, EventArgs e)
		{
			// The password text box should not be focused by default
			// in order to avoid a Caps Lock warning tooltip bug;
			// https://sourceforge.net/p/keepass/bugs/1807/
			Debug.Assert((m_tbPassword.TabIndex >= 2) && !m_tbPassword.Focused);

			GlobalWindowManager.AddWindow(this);

			BannerFactory.CreateBannerEx(this, m_bannerImage,
				Properties.Resources.B48x48_KGPG_Sign, KPRes.CreateMasterKey,
				m_ioInfo.GetDisplayName());
			this.Icon = AppIcons.Default;
			this.Text = KPRes.CreateMasterKey;

			FontUtil.SetDefaultFont(m_cbPassword);
			FontUtil.AssignDefaultBold(m_cbPassword);
			FontUtil.AssignDefaultBold(m_cbKeyFile);
			FontUtil.AssignDefaultBold(m_cbUserAccount);

			m_imgKeyFileWarning = UIUtil.IconToBitmap(SystemIcons.Warning,
				DpiUtil.ScaleIntX(16), DpiUtil.ScaleIntY(16));
			m_imgAccWarning = (Image)m_imgKeyFileWarning.Clone();
			m_picKeyFileWarning.Image = m_imgKeyFileWarning;
			m_picAccWarning.Image = m_imgAccWarning;

			UIUtil.ConfigureToolTip(m_ttRect);
			UIUtil.SetToolTip(m_ttRect, m_tbRepeatPassword, KPRes.PasswordRepeatHint, false);
			UIUtil.SetToolTip(m_ttRect, m_btnSaveKeyFile, KPRes.KeyFileCreate, false);
			UIUtil.SetToolTip(m_ttRect, m_btnOpenKeyFile, KPRes.KeyFileUseExisting, false);

			UIUtil.AccSetName(m_tbPassword, m_cbPassword);
			UIUtil.AccSetName(m_cmbKeyFile, m_cbKeyFile);
			UIUtil.AccSetName(m_picKeyFileWarning, KPRes.Warning);
			UIUtil.AccSetName(m_picAccWarning, KPRes.Warning);

			Debug.Assert(!m_lblIntro.AutoSize); // For RTL support
			if(!m_bCreatingNew)
				m_lblIntro.Text = KPRes.ChangeMasterKeyIntroShort;

			m_icgPassword.Attach(m_tbPassword, m_cbHidePassword, m_lblRepeatPassword,
				m_tbRepeatPassword, m_lblEstimatedQuality, m_pbPasswordQuality,
				m_lblQualityInfo, m_ttRect, this, true, false);

			m_cmbKeyFile.Items.Add(KPRes.NoKeyFileSpecifiedMeta);
			foreach(KeyProvider prov in Program.KeyProviderPool)
				m_cmbKeyFile.Items.Add(prov.Name);

			m_cmbKeyFile.SelectedIndex = 0;

			m_cbPassword.Checked = true;
			UIUtil.ApplyKeyUIFlags(Program.Config.UI.KeyCreationFlags,
				m_cbPassword, m_cbKeyFile, m_cbUserAccount, m_cbHidePassword);

			if(WinUtil.IsWindows9x || NativeLib.IsUnix())
			{
				UIUtil.SetChecked(m_cbUserAccount, false);
				UIUtil.SetEnabled(m_cbUserAccount, false);
				UIUtil.SetEnabled(m_lblWindowsAccDesc, false);
				UIUtil.SetEnabled(m_lblWindowsAccDesc2, false);
			}

			EnableUserControls();
			// UIUtil.SetFocus(m_tbPassword, this); // See OnFormShown
		}

		private void OnFormShown(object sender, EventArgs e)
		{
			// Focusing doesn't always work in OnFormLoad;
			// https://sourceforge.net/p/keepass/feature-requests/1735/
			if(m_tbPassword.CanFocus) UIUtil.ResetFocus(m_tbPassword, this, true);
			else if(m_cmbKeyFile.CanFocus) UIUtil.SetFocus(m_cmbKeyFile, this, true);
			else if(m_btnCreate.CanFocus) UIUtil.SetFocus(m_btnCreate, this, true);
			else { Debug.Assert(false); }
		}

		private void CleanUpEx()
		{
			if(m_imgKeyFileWarning != null)
			{
				m_picKeyFileWarning.Image = null;
				m_imgKeyFileWarning.Dispose();
				m_imgKeyFileWarning = null;
			}

			if(m_imgAccWarning != null)
			{
				m_picAccWarning.Image = null;
				m_imgAccWarning.Dispose();
				m_imgAccWarning = null;
			}

			m_icgPassword.Release();
		}

		private bool CreateCompositeKey()
		{
			m_pKey = new CompositeKey();

			if(m_cbPassword.Checked) // Use a password
			{
				if(!m_icgPassword.ValidateData(true)) return false;

				byte[] pb = m_icgPassword.GetPasswordUtf8();
				try
				{
					uint uPwLen = (uint)m_tbPassword.TextLength;
					uint uPwBits = QualityEstimation.EstimatePasswordBits(pb);

					uint uMinLen = Program.Config.Security.MasterPassword.MinimumLength;
					if(uPwLen < uMinLen)
					{
						string strML = KPRes.MasterPasswordMinLengthFailed;
						strML = strML.Replace(@"{PARAM}", uMinLen.ToString());
						MessageService.ShowWarning(strML);
						return false;
					}

					uint uMinQual = Program.Config.Security.MasterPassword.MinimumQuality;
					if(uPwBits < uMinQual)
					{
						string strMQ = KPRes.MasterPasswordMinQualityFailed;
						strMQ = strMQ.Replace(@"{PARAM}", uMinQual.ToString());
						MessageService.ShowWarning(strMQ);
						return false;
					}

					string strValRes = Program.KeyValidatorPool.Validate(pb,
						KeyValidationType.MasterPassword);
					if(strValRes != null)
					{
						MessageService.ShowWarning(strValRes);
						return false;
					}

					if(uPwLen == 0)
					{
						if(!MessageService.AskYesNo(KPRes.EmptyMasterPw +
							MessageService.NewParagraph + KPRes.EmptyMasterPwHint +
							MessageService.NewParagraph + KPRes.EmptyMasterPwQuestion,
							null, false))
							return false;
					}

					if(uPwBits <= PwDefs.QualityBitsWeak)
					{
						string strMQ = KPRes.MasterPasswordWeak + MessageService.NewParagraph +
							KPRes.MasterPasswordConfirm;
						if(!MessageService.AskYesNo(strMQ, null, false,
							MessageBoxIcon.Warning))
							return false;
					}

					m_pKey.AddUserKey(new KcpPassword(pb,
						Program.Config.Security.MasterPassword.RememberWhileOpen));
				}
				finally { MemUtil.ZeroByteArray(pb); }
			}

			string strKeyFile = m_cmbKeyFile.Text;
			bool bIsKeyProv = Program.KeyProviderPool.IsKeyProvider(strKeyFile);

			if(m_cbKeyFile.Checked && (!strKeyFile.Equals(KPRes.NoKeyFileSpecifiedMeta)) &&
				!bIsKeyProv)
			{
				try { m_pKey.AddUserKey(new KcpKeyFile(strKeyFile, true)); }
				catch(Exception exKF)
				{
					MessageService.ShowWarning(strKeyFile, KLRes.FileLoadFailed, exKF);
					return false;
				}
			}
			else if(m_cbKeyFile.Checked && (!strKeyFile.Equals(KPRes.NoKeyFileSpecifiedMeta)) &&
				bIsKeyProv)
			{
				KeyProviderQueryContext ctxKP = new KeyProviderQueryContext(
					m_ioInfo, true, false);

				bool bPerformHash;
				byte[] pbCustomKey = Program.KeyProviderPool.GetKey(strKeyFile, ctxKP,
					out bPerformHash);
				if((pbCustomKey != null) && (pbCustomKey.Length > 0))
				{
					try { m_pKey.AddUserKey(new KcpCustomKey(strKeyFile, pbCustomKey, bPerformHash)); }
					catch(Exception exCKP)
					{
						MessageService.ShowWarning(exCKP);
						return false;
					}

					MemUtil.ZeroByteArray(pbCustomKey);
				}
				else return false; // Provider has shown error message
			}

			if(m_cbUserAccount.Checked)
			{
				try { m_pKey.AddUserKey(new KcpUserAccount()); }
				catch(Exception exUA)
				{
					MessageService.ShowWarning(exUA);
					return false;
				}
			}

			return true;
		}

		private void EnableUserControls()
		{
			// Must support recursive call, see m_cbExpert.Checked
			// if(m_uBlockUpdate != 0) return;
			// ++m_uBlockUpdate;

			m_icgPassword.Enabled = m_cbPassword.Checked;

			bool bKeyFile = m_cbKeyFile.Checked;
			m_cmbKeyFile.Enabled = bKeyFile;

			string strKeyFile = m_cmbKeyFile.Text;
			bool bKeyProv = (!strKeyFile.Equals(KPRes.NoKeyFileSpecifiedMeta) &&
				Program.KeyProviderPool.IsKeyProvider(strKeyFile));
			m_btnOpenKeyFile.Enabled = m_btnSaveKeyFile.Enabled =
				(bKeyFile && !bKeyProv);

			bool bUserAccount = m_cbUserAccount.Checked;
			if(!m_cbPassword.Checked && !bKeyFile && !bUserAccount)
				m_btnCreate.Enabled = false;
			else if(bKeyFile && strKeyFile.Equals(KPRes.NoKeyFileSpecifiedMeta))
				m_btnCreate.Enabled = false;
			else m_btnCreate.Enabled = true;

			UIUtil.SetToolTip(m_ttRect, m_cmbKeyFile, strKeyFile, false);

			bool bExpert = m_cbExpert.Checked;
			bool bShowKF = (bExpert || bKeyFile);
			bool bShowUA = (bExpert || bUserAccount);

			Control[] vKeyFile = new Control[] {
				m_cbKeyFile, m_cmbKeyFile, m_btnOpenKeyFile, m_btnSaveKeyFile,
				m_lblKeyFileInfo, m_picKeyFileWarning, m_lblKeyFileWarning,
				m_lnkKeyFile
			};
			foreach(Control c in vKeyFile) c.Visible = bShowKF;

			Control[] vUserAccount = new Control[] {
				m_cbUserAccount, m_lblWindowsAccDesc, m_picAccWarning,
				m_lblWindowsAccDesc2, m_lnkUserAccount
			};
			foreach(Control c in vUserAccount) c.Visible = bShowUA;

			if(bKeyFile || bUserAccount)
			{
				if(!m_cbExpert.Checked)
					m_cbExpert.Checked = true; // Recursive, once

				m_cbExpert.Enabled = false;
			}
			else m_cbExpert.Enabled = true;

			// --m_uBlockUpdate;
		}

		private void OnCheckedPassword(object sender, EventArgs e)
		{
			EnableUserControls();

			if(m_cbPassword.Checked) UIUtil.SetFocus(m_tbPassword, this);
		}

		private void OnCheckedKeyFile(object sender, EventArgs e)
		{
			EnableUserControls();
		}

		private void OnBtnOK(object sender, EventArgs e)
		{
			if(!CreateCompositeKey()) this.DialogResult = DialogResult.None;
		}

		private void OnBtnCancel(object sender, EventArgs e)
		{
			m_pKey = null;
		}

		private void OnClickKeyFileCreate(object sender, EventArgs e)
		{
			KeyFileCreationForm dlg = new KeyFileCreationForm();
			dlg.InitEx(m_ioInfo);

			if(dlg.ShowDialog() == DialogResult.OK)
			{
				string strFile = dlg.ResultFile;
				if(!string.IsNullOrEmpty(strFile))
				{
					m_cmbKeyFile.Items.Add(strFile);
					m_cmbKeyFile.SelectedIndex = m_cmbKeyFile.Items.Count - 1;
				}
				else { Debug.Assert(false); }
			}

			UIUtil.DestroyForm(dlg);
			EnableUserControls();
		}

		private void OnClickKeyFileBrowse(object sender, EventArgs e)
		{
			string strFilter = AppDefs.GetKeyFileFilter();
			OpenFileDialogEx ofd = UIUtil.CreateOpenFileDialog(KPRes.KeyFileUseExisting,
				strFilter, 1, null, false, AppDefs.FileDialogContext.KeyFile);

			if(ofd.ShowDialog() != DialogResult.OK) return;

			string strFile = ofd.FileName;

			try
			{
				// Test whether we can read the file
				IOConnectionInfo ioc = IOConnectionInfo.FromPath(strFile);
				IOConnection.OpenRead(ioc).Close();

				// Check the file size?
			}
			catch(Exception ex) { MessageService.ShowWarning(ex); return; }

			if(!KfxFile.CanLoad(strFile))
			{
				if(!MessageService.AskYesNo(strFile + MessageService.NewParagraph +
					KPRes.KeyFileNoXml + MessageService.NewParagraph +
					KPRes.KeyFileUseAnywayQ, null, false))
					return;
			}

			m_cmbKeyFile.Items.Add(strFile);
			m_cmbKeyFile.SelectedIndex = m_cmbKeyFile.Items.Count - 1;

			EnableUserControls();
		}

		private void OnWinUserCheckedChanged(object sender, EventArgs e)
		{
			EnableUserControls();
		}

		private void OnFormClosed(object sender, FormClosedEventArgs e)
		{
			GlobalWindowManager.RemoveWindow(this);
		}

		private void OnBtnHelp(object sender, EventArgs e)
		{
			AppHelp.ShowHelp(AppDefs.HelpTopics.KeySources, null);
		}

		private void OnKeyFileSelectedIndexChanged(object sender, EventArgs e)
		{
			EnableUserControls();
		}

		private void OnFormClosing(object sender, FormClosingEventArgs e)
		{
			CleanUpEx();
		}

		private void OnKeyFileLinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			AppHelp.ShowHelp(AppDefs.HelpTopics.KeySources,
				AppDefs.HelpTopics.KeySourcesKeyFile);
		}

		private void OnUserAccountLinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			AppHelp.ShowHelp(AppDefs.HelpTopics.KeySources,
				AppDefs.HelpTopics.KeySourcesUserAccount);
		}

		private void OnExpertCheckedChanged(object sender, EventArgs e)
		{
			EnableUserControls();
		}
	}
}
