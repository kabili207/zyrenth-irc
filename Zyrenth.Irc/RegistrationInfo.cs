using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Zyrenth.Irc
{
	public class RegistrationInfo
	{
		public string[] NickNames { get; set; }
		public string UserName { get; set; }
		public string RealName { get; set; }
		public string Password { get; set; }

		public RegistrationInfo()
		{

		}

		public RegistrationInfo(string nick, string realname, string username)
			: this(new string[] { nick }, realname, username, "") { }

		public RegistrationInfo(string[] nicks, string realname, string username)
			: this(nicks, realname, username, "") { }

		public RegistrationInfo(string nick, string realname)
			: this(new string[] { nick }, realname, "", "") { }

		public RegistrationInfo(string[] nicks, string realname)
			: this(nicks, realname, "", "") { }

		public RegistrationInfo(string nick, string realname, string username, string password)
			: this(new string[] { nick }, realname, username, password) { }

		public RegistrationInfo(string[] nicks, string realname, string username, string password)
		{
			NickNames = nicks;
			UserName = username;
			RealName = realname;
			Password = password;
		}
	}
}
