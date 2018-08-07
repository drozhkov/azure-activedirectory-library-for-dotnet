﻿//----------------------------------------------------------------------
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

using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Clients.ActiveDirectory.Internal.Instance;
using Microsoft.IdentityModel.Clients.ActiveDirectory.Internal.OAuth2;
using Microsoft.Identity.Core.WsTrust;

namespace Microsoft.IdentityModel.Clients.ActiveDirectory.Internal.Flows
{
    internal class AcquireTokenNonInteractiveHandler : AcquireTokenHandlerBase
    {
        private readonly UserCredential userCredential;

        private UserAssertion userAssertion;

        public AcquireTokenNonInteractiveHandler(RequestData requestData, UserCredential userCredential)
            : base(requestData)
        {
            if (userCredential == null)
            {
                throw new ArgumentNullException("userCredential");
            }

            // We enable ADFS support only when it makes sense to do so
            if (requestData.Authenticator.AuthorityType == AuthorityType.ADFS)
            {
                this.SupportADFS = true;
            }

            this.userCredential = userCredential;
            this.DisplayableId = this.userCredential.UserName;
        }

        public AcquireTokenNonInteractiveHandler(RequestData requestData, UserAssertion userAssertion)
            : base(requestData)
        {
            if (userAssertion == null)
            {
                throw new ArgumentNullException("userAssertion");
            }

            if (string.IsNullOrWhiteSpace(userAssertion.AssertionType))
            {
                throw new ArgumentException(AdalErrorMessage.UserCredentialAssertionTypeEmpty, "userAssertion");
            }
            this.userAssertion = userAssertion;
        }

        protected override async Task PreRunAsync()
        {
            await base.PreRunAsync().ConfigureAwait(false);

            if (this.userCredential != null)
            {
                if (string.IsNullOrWhiteSpace(this.userCredential.UserName))
                {
                    this.userCredential.UserName = await platformInformation.GetUserPrincipalNameAsync().ConfigureAwait(false);
                    string msg;
                    if (string.IsNullOrWhiteSpace(userCredential.UserName))
                    {
                        msg = "Could not find UPN for logged in user";
                        RequestContext.Logger.Info(msg);
                        RequestContext.Logger.InfoPii(msg);

                        throw new AdalException(AdalError.UnknownUser);
                    }

                    msg = "Logged in user detected";
                    RequestContext.Logger.Verbose(msg);

                    var piiMsg = msg + string.Format(CultureInfo.CurrentCulture, " with user name '{0}'",
                                     userCredential.UserName);
                    RequestContext.Logger.VerbosePii(piiMsg);
                }

                this.DisplayableId = userCredential.UserName;
            }
            else if (this.userAssertion != null)
            {
                this.DisplayableId = userAssertion.UserName;
            }
        }

        protected override async Task PreTokenRequestAsync()
        {
            await base.PreTokenRequestAsync().ConfigureAwait(false);
            if (this.PerformUserRealmDiscovery())
            {
                var userRealmResponse = await Microsoft.Identity.Core.Realm.UserRealmDiscoveryResponse.CreateByDiscoveryAsync(this.Authenticator.UserRealmUri, this.userCredential.UserName, RequestContext).ConfigureAwait(false);
                if (userRealmResponse == null)
                {
                    throw new AdalException(AdalError.UserRealmDiscoveryFailed);
                }

                RequestContext.Logger.InfoPii(string.Format(CultureInfo.CurrentCulture,
                    " User with user name '{0}' detected as '{1}'", userCredential.UserName,
                    userRealmResponse.AccountType));

                if (string.Compare(userRealmResponse.AccountType, "federated", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    if (string.IsNullOrWhiteSpace(userRealmResponse.FederationMetadataUrl))
                    {
                        throw new AdalException(AdalError.MissingFederationMetadataUrl);
                    }

                    WsTrustAddress wsTrustAddress = null;
                    try
                    {
                        wsTrustAddress = await MexParser.FetchWsTrustAddressFromMexAsync(userRealmResponse.FederationMetadataUrl, this.userCredential.UserAuthType, RequestContext).ConfigureAwait(false);
                        if (wsTrustAddress == null)
                        {
                            if (this.userCredential.UserAuthType == Identity.Core.UserAuthType.IntegratedAuth)
                            {
                                throw new AdalException(AdalError.IntegratedAuthFailed, new AdalException(AdalError.WsTrustEndpointNotFoundInMetadataDocument));
                            }
                            else
                            {
                                throw new AdalException(AdalError.WsTrustEndpointNotFoundInMetadataDocument);
                            }
                        }
                    }
                    catch (System.Xml.XmlException ex)
                    {
                        throw new AdalException(AdalError.ParsingWsMetadataExchangeFailed, ex);
                    }
                    catch (AdalException ex)
                    {
                        throw new AdalException(AdalError.AccessingWsMetadataExchangeFailed, ex);
                    }
                    RequestContext.Logger.InfoPii(string.Format(CultureInfo.CurrentCulture, " WS-Trust endpoint '{0}' fetched from MEX at '{1}'",
                            wsTrustAddress.Uri, userRealmResponse.FederationMetadataUrl));

                    WsTrustResponse wsTrustResponse;
                    try
                    {
                        wsTrustResponse = await WsTrustRequest.SendRequestAsync(wsTrustAddress, this.userCredential.GetUserCredential(), RequestContext, userRealmResponse.CloudAudienceUrn).ConfigureAwait(false);
                    }
                    catch (AdalServiceException ex)
                    {
                        throw new AdalException(AdalError.ParsingWsTrustResponseFailed, ex.Message, ex);
                    }
                    if (wsTrustResponse == null)
                    {
                        throw new AdalException(AdalError.ParsingWsTrustResponseFailed);
                    }


                    var msg = string.Format(CultureInfo.CurrentCulture,
                        " Token of type '{0}' acquired from WS-Trust endpoint", wsTrustResponse.TokenType);
                    RequestContext.Logger.Info(msg);
                    RequestContext.Logger.InfoPii(msg);

                    // We assume that if the response token type is not SAML 1.1, it is SAML 2
                    this.userAssertion = new UserAssertion(wsTrustResponse.Token, (wsTrustResponse.TokenType == WsTrustResponse.Saml1Assertion) ? OAuthGrantType.Saml11Bearer : OAuthGrantType.Saml20Bearer);
                }
                else if (string.Compare(userRealmResponse.AccountType, "managed", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    // handle password grant flow for the managed user
                    if (this.userCredential.PasswordToCharArray() == null)
                    {
                        throw new AdalException(AdalError.PasswordRequiredForManagedUserError);
                    }
                }
                else
                {
                    throw new AdalException(AdalError.UnknownUserType);
                }
            }
        }

        protected override void AddAditionalRequestParameters(DictionaryRequestParameters requestParameters)
        {
            if (this.userAssertion != null)
            {
                requestParameters[OAuthParameter.GrantType] = this.userAssertion.AssertionType;
                requestParameters[OAuthParameter.Assertion] = Convert.ToBase64String(Encoding.UTF8.GetBytes(this.userAssertion.Assertion));
            }
            else
            {
                this.userCredential.ApplyTo(requestParameters);
            }

            // To request id_token in response
            requestParameters[OAuthParameter.Scope] = OAuthValue.ScopeOpenId;
        }

        private bool PerformUserRealmDiscovery()
        {
            // To decide whether user realm discovery is needed or not
            // we should also consider if that is supported by the authority
            return this.userAssertion == null && !this.SupportADFS;
        }
    }
}
