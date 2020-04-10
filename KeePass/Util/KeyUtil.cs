/*
  KeePass Password Safe - The Open-Source Password Manager
  Copyright (C) 2003-2007 Dominik Reichl <dominik.reichl@t-online.de>

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
using System.Text;
using System.Windows.Forms;

using KeePass.App;
using KeePass.Resources;

using KeePassLib;
using KeePassLib.Keys;

namespace KeePass.Util
{
	public static class KeyUtil
	{
		public static CompositeKey KeyFromCommandLine()
		{
			CompositeKey cmpKey = new CompositeKey();

			string strPassword = Program.CommandLineArgs[AppDefs.CommandLineOptions.Password];
			string strKeyFile = Program.CommandLineArgs[AppDefs.CommandLineOptions.KeyFile];
			string strUserAcc = Program.CommandLineArgs[AppDefs.CommandLineOptions.UserAccount];

			if((strPassword == null) && (strKeyFile == null))
				return null;

			if(strPassword != null)
				cmpKey.AddUserKey(new KcpPassword(strPassword));
			if(strKeyFile != null)
			{
				try { cmpKey.AddUserKey(new KcpKeyFile(strKeyFile)); }
				catch(Exception)
				{
					MessageBox.Show(strKeyFile + "\r\n\r\n" + KPRes.KeyFileError,
						PwDefs.ShortProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return null;
				}
			}
			if(strUserAcc != null)
				cmpKey.AddUserKey(new KcpUserAccount());

			return cmpKey;
		}
	}
}
