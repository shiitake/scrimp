using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Net.Http.Headers;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.Hosting;
using Scrimp.Models;
using Microsoft.Extensions.Logging;


namespace Scrimp
{
    //this service connects to the website and downloads the list of episodes including their playlist location
    public class EpisodeService : BackgroundService
    {
        private readonly ILogger<EpisodeService> _logger;
        private readonly string _username;
        private readonly string _password;
        private const string _baseUrl = "https://specificwebsite.cn";
        private const string _showTitle = "show-title";

        public EpisodeService(ILogger<EpisodeService> logger, string username, string password)
        {
            _logger = logger;
            _username = username;
            _password = password;
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogDebug($"Episode Service is Starting");

            stoppingToken.Register(() =>
                _logger.LogDebug($"Episode Service task is stopping."));

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Episode service task is starting.");
                var loginCookies = Login("login").Result;
                if (loginCookies != null)
                {
                    //get episodes	
                    string episodeUrl = $"{_baseUrl}/watch/show/{_showTitle}";
                    var response = CallUrl(episodeUrl, loginCookies).Result;
                
                
                    var html = response.Content.ReadAsStringAsync().Result;		
                    var epList = GetEpisodeList(html);
                    var downloaded = DownloadEpisode(epList.First(),loginCookies).Result;
                    if (downloaded)
                    {
                        _logger.LogInformation($"Episode downloaded: {epList.First().Title}");
                    }
                }
                else
                {
                    _logger.LogWarning("No login cookies.");
                
                }

                await Task.Delay();
            }
 
        }

        public async Task<bool> DownloadEpisode(Episode episode, IDictionary<string, string> cookies)
        {
            //this will connect to the show page and get the playlist info for the video
            var showUrl = $"{_baseUrl}{episode.Page}";
            var showPage = CallUrl(showUrl, cookies).Result;
	
            //get source link
            var html = showPage.Content.ReadAsStringAsync().Result;
            var videoSource = GetVideoSource(html);
	
            //download source data
            var videoPlaylist = CallUrl(videoSource).Result;
            _logger.LogInformation("Video Playlist", videoPlaylist.Content.ReadAsStringAsync().Result);
            //episode.Dump();
	
            if (string.IsNullOrWhiteSpace(episode.VideoSource)){
                _logger.LogError("Video Source not found");
                return false;
            }
            else {
                return true;
                //ffmeg magic
                //var videoStream = new Stream();
                //var streamSource = new StreamPipeSource(episode.VideoSource);
                //await FFMpegArguments
                //.FromPipeInput(
                //
                //await FFMpegArguments
                //.FromFileInput(videoOutput)
                //.OutputToFile(contentFolder + videoInfo.Id + "_playlist_1080p.m3u8", false, options => options
                //	.WithVideoCodec(VideoCodec.LibX264)
                //	.WithConstantRateFactor(21)
                //	.WithAudioCodec(AudioCodec.Aac)
                //	.WithVariableBitrate(4)
                //	.UsingMultithreading(false)
                //	.WithVideoFilters(filterOptions => filterOptions
                //		.Scale(VideoSize.FullHd))
                //	.WithFastStart())
                //.ProcessAsynchronously();
            }



        }

        
        private List<Episode> GetEpisodeList(string html)
        {	
            HtmlDocument htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);
            var programmerLinks = htmlDoc.DocumentNode.Descendants("div")
                .Where(node => node.GetAttributeValue("class", "").Contains("card my-3 my-lg-0")).ToList();
	
            List<Episode> episodeList = new List<Episode>();
            TextInfo myTI = new CultureInfo("en-US", false).TextInfo;

            foreach (var link in programmerLinks)
            {
                var episode = link.ChildNodes
                    .Where(cn => cn.GetAttributeValue("href", "").Contains($"{_showTitle}"))
                    .Select(cn =>
                        new Episode
                        {
                            Page = cn.Attributes[0].Value,
                            Description = link.Descendants("p").Where(cn => cn.GetAttributeValue("class", "").Contains("card-text")).Select(cn => cn.InnerHtml).First()
                        }).First();

                var name = link.Descendants("h5").Where(l => l.GetAttributeValue("class", "").Contains("card-title")).Select(cn => myTI.ToTitleCase(myTI.ToLower(cn.InnerHtml))).First();
                var firstDash = name.IndexOf('-');
                var title = name[(firstDash + 1)..].Trim();
                episode.Title = title;

                var episodeNumber = name.Substring(0, firstDash).Trim();
                var eps = episodeNumber.Split(new[] { 'S', 'e' }, 3);

                if (int.TryParse(eps[1], out int seasonNumber))
                {
                    episode.Season = seasonNumber;
                }
                if (int.TryParse(eps[2], out int epNumber))
                {
                    episode.Number = epNumber;
                }

                var rawDate = link.Descendants("small").Where(l => l.GetAttributeValue("class", "").Contains("text-muted")).Select(cn => cn.InnerHtml).First();
                //remove day suffixes
                var airDate = Regex.Replace(rawDate, "st|nd|rd|th", "", RegexOptions.Multiline);
                var dt = DateTime.Parse(airDate);
                episode.AirDate = dt;
                episodeList.Add(episode);		
            }
            return episodeList;
        }

        private string GetVideoSource(string html)
        {
            HtmlDocument htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);
            var sourceLink = htmlDoc.DocumentNode.Descendants("source")
                .Where(node => node.GetAttributeValue("type", "").Contains("application/x-mpegURL"))
                .Select(node => node.GetAttributeValue("src", ""))
                .First();

            return sourceLink;
        }

        public async Task<HttpResponseMessage> CallUrl(string fullUrl, IDictionary<string, string> cookies = null)
        {
            var cookiesContainer = new CookieContainer();
            if (cookies != null)
            {
                cookiesContainer = CreateCookieContainer(new Uri(fullUrl), cookies);
            }

            using var handler = new HttpClientHandler() { CookieContainer = cookiesContainer };
            using var client = new HttpClient(handler);
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls13;		
            client.DefaultRequestHeaders.Accept.Clear();		
            return await client.GetAsync(fullUrl);
        }

        private string GetLoginToken(string html)
        {
            HtmlDocument htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);
            var form = htmlDoc.DocumentNode.Descendants("form")
                .Where(node => node.GetAttributeValue("action", "").Contains("login"))			
                .First();	

            var token = form.Descendants().Where(t =>
                    t.NodeType == HtmlNodeType.Element &&
                    t.GetAttributeValue("name", "").Contains("_token"))
                .Select(n => n.Attributes["value"].Value).First();	
	
            return token;
        }

        public async Task<IDictionary<string, string>> Login(string loginUrl)
        {	
            var loginPage = CallUrl($"{_baseUrl}/{loginUrl}").Result;	
            var loginHtml = await loginPage.Content.ReadAsStringAsync();
            var token = GetLoginToken(loginHtml);
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogError("Login token is missing");
                return null;
            }
            
            var baseAddress = new Uri(_baseUrl);	
            
            //get cookies
            var cookies = ExtractCookiesFromResponse(loginPage);
            var cookieContainer = CreateCookieContainer(baseAddress, cookies);

            using var handler = new HttpClientHandler() { CookieContainer = cookieContainer };
            using var client = new HttpClient(handler) { BaseAddress = baseAddress };
            var UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/90.0.4430.72 Safari/537.36";			
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-www-form-urlencoded"));
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls13;
            var request = new HttpRequestMessage(HttpMethod.Post, loginUrl);
            request.Headers.Add("User-Agent", UserAgent);

            FormUrlEncodedContent postData = new FormUrlEncodedContent( new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("email", _username),
                new KeyValuePair<string, string>("password", _password),
                new KeyValuePair<string, string>("remember", "on"),
                new KeyValuePair<string, string>("_token", token)
            });
            request.Content = postData;			

            // Send request and get response
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Login failed: {response.StatusCode}");
                return null;
            }		
            _logger.LogInformation("Login successful");
            return ExtractCookiesFromResponse(response);
        }

        private CookieContainer CreateCookieContainer(Uri baseAddress, IDictionary<string, string> cookies)
        {
            // also add cookie-consent-status
            var container = new CookieContainer();
            container.Add(baseAddress, new Cookie("cookieconsent_status", "dismiss"));
            cookies.Keys.ToList().ForEach(key =>
            {
                container.Add(baseAddress, new Cookie(key, cookies[key]));
            });
            return container;	
        }

        private IDictionary<string, string> ExtractCookiesFromResponse(HttpResponseMessage response)
        {
            IDictionary<string, string> result = new Dictionary<string, string>();
            if (response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string> values))
            {
                SetCookieHeaderValue.ParseList(values.ToList()).ToList().ForEach(cookie =>
                {
                    result.Add(cookie.Name.ToString(), cookie.Value.ToString());
                });
            }
            return result;
        }

    }
}
