//----------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.
// All rights reserved.
//
// This code is licensed under the MIT License.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files(the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and / or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions :
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
//------------------------------------------------------------------------------

using Microsoft.IdentityModel.Clients.ActiveDirectory.Internal;
using Microsoft.IdentityModel.Clients.ActiveDirectory.Internal.OAuth2;
using System;
using System.Runtime.InteropServices;
using System.Security;

namespace Microsoft.IdentityModel.Clients.ActiveDirectory
{
    /// <summary>
    /// Credential used for username/password authentication.
    /// </summary>
    public sealed class UserPasswordCredential : UserCredential
    {
        /// <summary>
        /// Constructor to create credential with username and password
        /// </summary>
        /// <param name="userName">Identifier of the user application requests token on behalf.</param>
        /// <param name="password">User password.</param>
        public UserPasswordCredential(string userName, string password):base(userName, UserAuthType.UsernamePassword)
        {
            this.Password = password;
        }

        internal string Password { get; }

        internal override char[] PasswordToCharArray()
        {
            return (this.Password != null) ? this.Password.ToCharArray() : null;
        }

        internal override void ApplyTo(DictionaryRequestParameters requestParameters)
        {
            requestParameters[OAuthParameter.GrantType] = OAuthGrantType.Password;
            requestParameters[OAuthParameter.Username] = this.UserName;
            requestParameters[OAuthParameter.Password] = new string(PasswordToCharArray());
        }
    }
}