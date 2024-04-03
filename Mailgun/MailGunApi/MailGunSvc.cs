using System;
using System.Collections.Generic;
using System.Text;
using System.Data;
using Aspectize.Core;
using System.Security.Permissions;
using System.Net.Http;
using System.Net.Http.Headers;

namespace MailGunApi {

    class HttpClient {

        const string Basic = "Basic";
        static string getBasicAuthorizationHeaderValue(string userName, string password) {

            var name_and_pwd = String.Format("{0}:{1}", userName, password ?? "");
            var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(name_and_pwd));
            return b64;
        }

        internal const string pFrom = "from";
        internal const string pTo = "to";
        internal const string pBcc = "bcc";
        internal const string pSubject = "subject";
        internal const string pHtml = "html";
        internal const string pText = "text";
        internal const string pAttachment = "attachment";

        internal const string pAttachments = "ATTACHMENTS";

        static void addData(MultipartFormDataContent data, string fieldName, object fieldValue) {

            switch (fieldName) {
                case pFrom:
                case pTo:
                case pSubject:
                case pHtml:
                case pText:
                    data.Add(new StringContent(fieldValue.ToString()), fieldName);
                    break;

                case pAttachments: {

                        var attachments = fieldValue as Dictionary<string, byte[]>;

                        foreach (var fileName in attachments.Keys) {

                            var fileBytes = attachments[fileName];

                            var fileContent = new ByteArrayContent(fileBytes);
                            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data");

                            data.Add(fileContent, pAttachment, fileName);
                        }
                    }
                    break;

            }
        }

        static internal string Post(string url, Dictionary<string, object> parameters, string userName, string passWord) {

            if (!parameters.ContainsKey(pHtml)) {

                parameters.Add(pHtml, parameters.ContainsKey(pText) ? parameters[pText] : string.Empty);
            }

            var postData = new MultipartFormDataContent();
            foreach (var p in parameters.Keys) {

                addData(postData, p, parameters[p]);
            }

            
            var client = new System.Net.Http.HttpClient();
            var encodedCredentials = getBasicAuthorizationHeaderValue(userName, passWord);
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(Basic, encodedCredentials);

            var response = client.PostAsync(url, postData);
            response.Wait();
            client.Dispose();

            var success = response.Result.IsSuccessStatusCode;
            var tContent = response.Result.Content.ReadAsStringAsync();
            tContent.Wait();

            //{"id":"<20240403180533.9b900b9b62429cc6@mg.aspectize.com>","message":"Queued. Thank you."}
            var result = tContent.Result;

            return result;
        }
    }

    public interface IMailGunSvc {

        [CommandAttribute(Bindable = false, ServerOnly = true)]
        void SendMailSimple(bool sendCopyToExpediteur, string destinataire, string subject, string message);

        [CommandAttribute(Bindable = false, ServerOnly = true)]
        void SendMailFrom(string expediteur, string[] destinataires, string subject, string message);

        [CommandAttribute(Bindable = false, ServerOnly = true)]
        void SendMail(bool sendCopyToExpediteur, string[] destinataires, string subject, string message, Dictionary<string, byte[]> dicoAttachements);

        [CommandAttribute(Bindable = false, ServerOnly = true)]
        void SendMailWithBcc(bool sendCopyToExpediteur, string[] destinataires, string subject, string message, Dictionary<string, byte[]> dicoAttachements, string[] bcc);

        void Test();
    }


    [Service(Name = "MailGunSvc")]
    public class MailGunSvc : IMailGunSvc //, IInitializable, ISingleton
    {

        const string mgUrl = "https://api.eu.mailgun.net/v3";

        [Parameter(Name = "ApiKey")]
        string ApiKey = string.Empty;

        [Parameter(Name = "VerifiedSendingDomain")]
        string VerifiedSendingDomain = string.Empty;

        [Parameter(Name = "DefaultSender")]
        string DefaultSender = string.Empty;

        [Parameter(Name = "DefaultSenderDisplay")]
        string DefaultSenderDisplay = string.Empty;

        string sendMailGunApi(string fromDisplay, string from, string[] to, string subject, bool htmlMessage, string message, Dictionary<string, byte[]> dicoAttachements, string[] bcc) {

            var url = $"{mgUrl}/{VerifiedSendingDomain}/messages";

            var mgFrom = from;
            if (!string.IsNullOrWhiteSpace(fromDisplay)) {

                mgFrom = $"{fromDisplay} <{from}>";
            }
            var mgTo = string.Join(", ", to);

            var mgParams = new Dictionary<string, object>();

            mgParams.Add(HttpClient.pFrom, mgFrom);
            mgParams.Add(HttpClient.pTo, mgTo);
            mgParams.Add(HttpClient.pSubject, subject);
            mgParams.Add(htmlMessage ? HttpClient.pHtml : HttpClient.pText, message);

            if (bcc != null) {
                var mgBcc = string.Join(", ", bcc);
                mgParams.Add(HttpClient.pBcc, mgBcc);
            }

            if (dicoAttachements != null) {
                mgParams.Add(HttpClient.pAttachments, dicoAttachements);
            }

            return HttpClient.Post(url, mgParams, "api", ApiKey);
        }


        void IMailGunSvc.SendMail(bool sendCopyToExpediteur, string[] destinataires, string subject, string message, Dictionary<string, byte[]> dicoAttachements) {

            if (string.IsNullOrWhiteSpace(DefaultSender)) throw new SmartException("Missing DefaultSender in MailGunSvc config");

            if (sendCopyToExpediteur) {

                var l = new List<string>(destinataires);
                l.Add(DefaultSender);

                destinataires = l.ToArray();
            }

            sendMailGunApi(DefaultSenderDisplay, DefaultSender, destinataires, subject, true, message, dicoAttachements, null);
        }

        void IMailGunSvc.SendMailFrom(string expediteur, string[] destinataires, string subject, string message) {

            var dsd = DefaultSenderDisplay;

            if (string.IsNullOrWhiteSpace(expediteur)) {

                if (string.IsNullOrWhiteSpace(DefaultSender)) throw new SmartException("Missing expediteur and DefaultSender in MailGunSvc config");

                expediteur = DefaultSender;

            } else dsd = string.Empty;

            sendMailGunApi(dsd, expediteur, destinataires, subject, true, message, null, null);

        }

        void IMailGunSvc.SendMailSimple(bool sendCopyToExpediteur, string destinataire, string subject, string message) {

            if (string.IsNullOrWhiteSpace(DefaultSender)) throw new SmartException("Missing DefaultSender in MailGunSvc config");

            var destinataires = sendCopyToExpediteur ? new string[] { destinataire, DefaultSender } : new string[] { destinataire };

            sendMailGunApi(DefaultSenderDisplay, DefaultSender, destinataires, subject, true, message, null, null);
        }

        void IMailGunSvc.SendMailWithBcc(bool sendCopyToExpediteur, string[] destinataires, string subject, string message, Dictionary<string, byte[]> dicoAttachements, string[] bcc) {

            if (string.IsNullOrWhiteSpace(DefaultSender)) throw new SmartException("Missing DefaultSender in MailGunSvc config");

            if (sendCopyToExpediteur) {

                var l = new List<string>(destinataires);
                l.Add(DefaultSender);

                destinataires = l.ToArray();
            }

            sendMailGunApi(DefaultSenderDisplay, DefaultSender, destinataires, subject, true, message, dicoAttachements, bcc);
        }

        void IMailGunSvc.Test() {

            try {
                var dico = new Dictionary<string, byte[]>();
                var bytes = System.IO.File.ReadAllBytes(@"D:\DriveS\Delivery\Applications\ABugTest\Data\test.pdf");
                dico.Add("palette.pdf", bytes);
                sendMailGunApi("Hey Fredy", "fredy@aspectize.com", new[] { "frederic.fadel@gmail.com" }, "test mail gun", true, "test message", dico, new[] { "frederic.fadel@hotmail.com" });

            } catch (Exception x) {

                var m = x.Message;
            }

        }
    }

}
