﻿
#region License
/*
 **************************************************************
 *  Author: Rick Strahl
 *          © West Wind Technologies, 2016
 *          http://www.west-wind.com/
 *
 * Created: 05/15/2016
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 **************************************************************
*/
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FontAwesome.WPF;
using HtmlAgilityPack;
using WebLogAddin.MetaWebLogApi;
using MarkdownMonster;
using MarkdownMonster.AddIns;
using WebLogAddin.LocalJekyll;
using WebLogAddin.Medium;
using Westwind.Utilities;
using File = System.IO.File;

namespace WeblogAddin
{
	public class WebLogAddin :  MarkdownMonsterAddin, IMarkdownMonsterAddin
	{
		public WeblogAddinModel WeblogModel { get; set; } = new WeblogAddinModel();

		public WeblogForm WeblogForm { get; set; }

		#region Addin Events
		public override void OnApplicationStart()
		{
			base.OnApplicationStart();

			WeblogModel = new WeblogAddinModel()
			{
				Addin = this,
			};

			Id = "weblog";
			Name = "Weblog Publishing Addin";

			// Create addin and automatically hook menu events
			var menuItem = new AddInMenuItem(this)
			{
				Caption = "Weblog Publishing",
				FontawesomeIcon = FontAwesomeIcon.Rss,
				KeyboardShortcut = WeblogAddinConfiguration.Current.KeyboardShortcut
			};
			try
			{
				menuItem.IconImageSource = new ImageSourceConverter()
						.ConvertFromString("pack://application:,,,/WeblogAddin;component/icon_22.png") as ImageSource;
			}
			catch { }


			MenuItems.Add(menuItem);
		}

		public override void OnWindowLoaded()
		{
			base.OnWindowLoaded();

			AddMainMenuItems();
		}

		public override void OnExecute(object sender)
		{
			// read settings on startup
			WeblogAddinConfiguration.Current.Read();

			WeblogForm?.Close();
			WeblogForm = new WeblogForm(WeblogModel)
			{
				Owner = Model.Window
			};
			WeblogModel.AppModel = Model;

			WeblogForm.Show();
		}


		public override bool OnCanExecute(object sender)
		{
			return Model.IsEditorActive;
		}

		public override void OnExecuteConfiguration(object sender)
		{
			string file = Path.Combine(mmApp.Configuration.CommonFolder, "weblogaddin.json");
			Model.Window.OpenTab(file);
		}


		public override void OnNotifyAddin(string command, object parameter)
		{
			if (command == "newweblogpost")
				WeblogFormCommand.Execute("newweblogpost");
		}
		#endregion

		#region Post Send Operations

		/// <summary>
		/// High level method that sends posts to the Weblog
		///
		/// </summary>
		/// <returns></returns>
		public async Task<bool> SendPost(WeblogInfo weblogInfo, bool sendAsDraft = false)
		{
            
			var editor = Model.ActiveEditor;
			if (editor == null)
				return false;

			var doc = editor.MarkdownDocument;

			WeblogModel.ActivePost = new Post()
			{
				DateCreated = DateTime.Now
			};

			// start by retrieving the current Markdown from the editor
			string markdown = editor.MarkdownDocument.CurrentText;

			// Retrieve Meta data from post and clean up the raw markdown
			// so we render without the config data
			var meta = WeblogPostMetadata.GetPostConfigFromMarkdown(markdown, WeblogModel.ActivePost, weblogInfo);

			string html = doc.RenderHtml(meta.MarkdownBody, usePragmaLines: false);
			WeblogModel.ActivePost.Body = html;
			WeblogModel.ActivePost.PostId = meta.PostId;
			WeblogModel.ActivePost.PostStatus = meta.PostStatus;
			WeblogModel.ActivePost.Permalink = meta.Permalink;

			// Custom Field Processing:
			// Add custom fields from existing post
			// then add or update our custom fields
			var customFields = new Dictionary<string, CustomField>();

			// load existing custom fields from post online if possible
			if (!string.IsNullOrEmpty(meta.PostId))
			{
				var existingPost = GetPost(meta.PostId, weblogInfo);
				if (existingPost != null && meta.CustomFields != null && existingPost.CustomFields != null)
				{
					foreach (var kvp in existingPost.CustomFields)
					{
						if (!customFields.ContainsKey(kvp.Key))
							AddOrUpdateCustomField(customFields, kvp.Key, kvp.Value);
					}
				}
			}
			// add custom fields from Weblog configuration
			if (weblogInfo.CustomFields != null)
			{
				foreach (var kvp in weblogInfo.CustomFields)
				{
					AddOrUpdateCustomField(customFields, kvp.Key, kvp.Value);
				}
			}
			// add custom fields from Meta data
			if (meta.CustomFields != null)
			{
				foreach (var kvp in meta.CustomFields)
				{
					AddOrUpdateCustomField(customFields, kvp.Key, kvp.Value.Value);
				}
			}

			if (!string.IsNullOrEmpty(markdown))
			{
				AddOrUpdateCustomField(customFields, "mt_markdown", markdown);
			}

			WeblogModel.ActivePost.CustomFields = customFields.Values.ToArray();

			var config = WeblogAddinConfiguration.Current;

			var kv = config.Weblogs.FirstOrDefault(kvl => kvl.Value.Name == meta.WeblogName);
			if (kv.Equals(default(KeyValuePair<string, WeblogInfo>)))
			{
				MessageBox.Show(WeblogForm, "Invalid Weblog configuration selected.",
					"Weblog Posting Failed",
					MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return false;
			}
			weblogInfo = kv.Value;

			var type = weblogInfo.Type;
			if (type == WeblogTypes.Unknown)
				type = weblogInfo.Type;


            string previewUrl = weblogInfo.PreviewUrl;
			string basePath = Path.GetDirectoryName(doc.Filename);
			string postUrl = null;

			if (type == WeblogTypes.MetaWeblogApi || type == WeblogTypes.Wordpress)
			{
				MetaWebLogWordpressApiClient client;
				client = new MetaWebLogWordpressApiClient(weblogInfo);

				// if values are already configured don't overwrite them again
				client.DontInferFeaturedImage = meta.DontInferFeaturedImage;
				client.FeaturedImageUrl = meta.FeaturedImageUrl;
				client.FeatureImageId = meta.FeaturedImageId;

				var result = await Task.Run<bool>(() => client.PublishCompletePost(WeblogModel.ActivePost, basePath,
					sendAsDraft, markdown));


                meta.FeaturedImageUrl = client.FeaturedImageUrl;
                meta.FeaturedImageId = client.FeatureImageId;

				//if (!client.PublishCompletePost(WeblogModel.ActivePost, basePath,
				//    sendAsDraft, markdown))
				if (!result)
				{
					mmApp.Log($"Error sending post to Weblog at {weblogInfo.ApiUrl}: " + client.ErrorMessage);
					MessageBox.Show(WeblogForm, "Error sending post to Weblog: " + client.ErrorMessage,
						mmApp.ApplicationName,
						MessageBoxButton.OK,
						MessageBoxImage.Exclamation);
					return false;
				}

				var post = client.GetPost(WeblogModel.ActivePost.PostId);
				if (post != null)
				{
					postUrl = post.Url;
					meta.Permalink = post.Permalink;
                    meta.PostId = post.PostId?.ToString();
                    WeblogModel.ActivePost.PostId = meta.PostId;
                    WeblogModel.ActivePost.Permalink = post.Permalink;
                }
			}
			if (type == WeblogTypes.Medium)
			{
				var client = new MediumApiClient(weblogInfo);
				var post = client.PublishCompletePost(WeblogModel.ActivePost, basePath, sendAsDraft);
				if (post == null)
				{
					mmApp.Log($"Error sending post to Weblog at {weblogInfo.ApiUrl}: " + client.ErrorMessage);
					MessageBox.Show(WeblogForm, client.ErrorMessage,
						"Error Sending Post to Medium",
						MessageBoxButton.OK,
						MessageBoxImage.Exclamation);
					return false;
				}

				// this is null
				postUrl = client.PostUrl;
			}

			if (type == WeblogTypes.LocalJekyll)
			{
				var pub = new LocalJekyllPublisher(meta, weblogInfo, Model.ActiveDocument.Filename);
				pub.PublishPost(false);

                if (!string.IsNullOrEmpty(weblogInfo.LaunchCommand))
                {
                    if (!pub.BuildAndLaunchSite())
                    {
                        ShowStatusError(pub.ErrorMessage);
                        return false;
                    }
                    previewUrl = null;
                    postUrl = pub.GetPostUrl(weblogInfo.PreviewUrl ?? "http://localhost:4000/{0}");
                }
			}

			meta.PostId = WeblogModel.ActivePost.PostId?.ToString();

			// retrieve the raw editor markdown
			markdown = editor.MarkdownDocument.CurrentText;
			meta.RawMarkdownBody = markdown;

			// add the meta configuration to it
			markdown = meta.SetPostYamlFromMetaData();

			// write it back out to editor
			editor.SetMarkdown(markdown, updateDirtyFlag: true, keepUndoBuffer: true);

			try
            {
                if (!string.IsNullOrEmpty(postUrl))
                    ShellUtils.GoUrl(postUrl);
                else if (!string.IsNullOrEmpty(previewUrl))
				{
					var url = string.Format(previewUrl, WeblogModel.ActivePost.PostId);
					ShellUtils.GoUrl(url);
				}
				else
				{
					ShellUtils.GoUrl(new Uri(weblogInfo.ApiUrl).GetLeftPart(UriPartial.Authority));
				}
			}
			catch
			{
				mmApp.Log("Failed to display Weblog Url after posting: " +
						  weblogInfo.PreviewUrl ?? postUrl ?? weblogInfo.ApiUrl);
			}

			return true;
		}


		/// <summary>
		/// Returns a Post by Id
		/// </summary>
		/// <param name="postId"></param>
		/// <param name="weblogInfo"></param>
		/// <returns></returns>
		public Post GetPost(string postId, WeblogInfo weblogInfo)
		{
			if (weblogInfo.Type == WeblogTypes.MetaWeblogApi || weblogInfo.Type == WeblogTypes.Wordpress)
			{
				MetaWebLogWordpressApiClient client;
				client = new MetaWebLogWordpressApiClient(weblogInfo);
				return client.GetPost(postId);
			}

            if (weblogInfo.Type == WeblogTypes.LocalJekyll)
            {
                var pub = new LocalJekyllPublisher(null, weblogInfo, null);
                return pub.GetPost(postId);
            }

			// Medium doesn't support post retrieval so return null
			return null;
		}

		private void AddOrUpdateCustomField(IDictionary<string, CustomField> customFields, string key, string value)
		{
			CustomField cf;
			if (customFields.TryGetValue(key, out cf))
			{
				cf.Value = value;
			}
			else
			{
				customFields.Add(key, new CustomField { Key = key, Value = value });
			}
		}

#endregion

#region Local Post Creation

		public string NewWeblogPost(WeblogPostMetadata meta)
		{
			if (meta == null)
			{
				meta = new WeblogPostMetadata()
				{
					Title = "Post Title",
					MarkdownBody = string.Empty
				};
			}


			if (string.IsNullOrEmpty(meta.WeblogName))
				meta.WeblogName = "Name of registered blog to post to";

			bool hasFrontMatter = meta.MarkdownBody != null &&
								  (meta.MarkdownBody.TrimStart().StartsWith("---\n") ||
								   meta.MarkdownBody.TrimStart().StartsWith("---\r"));
			string post;

			if (hasFrontMatter)
				post = meta.MarkdownBody;
			else
				post =
				$@"# {meta.Title}

{meta.MarkdownBody}
";
			meta.RawMarkdownBody = post;
			meta.MarkdownBody = post;

			if(!hasFrontMatter)
				post = meta.SetPostYamlFromMetaData();

			return post;
		}

		public void CreateNewPostOnDisk(string title, string postFilename, string weblogName)
		{
			string filename = FileUtils.SafeFilename(postFilename);
			string titleFilename = GetPostFileNameFromTitle(title);

			var mmPostFolder = Path.Combine(WeblogAddinConfiguration.Current.PostsFolder,DateTime.Now.Year + "-" + DateTime.Now.Month.ToString("00"),titleFilename);
			if (!Directory.Exists(mmPostFolder))
				Directory.CreateDirectory(mmPostFolder);
			var outputFile = Path.Combine(mmPostFolder, filename);

			// Create the new post by creating a file with title preset
			string newPostMarkdown = NewWeblogPost(new WeblogPostMetadata()
			{
				Title = title,
				WeblogName = weblogName
			});

			string msg = null;
			Exception ex = null;
			try
			{
				File.WriteAllText(outputFile, newPostMarkdown);

				if (!File.Exists(outputFile))
					msg = "Couldn't create the Weblog Post output file.";
			}
			catch (Exception exc)
			{
				ex = exc;
				msg = ex.Message;
			}

			if (msg != null)
			{
				MessageBox.Show(WeblogForm, $"Couldn't write new Weblog Post file:\r\n\r\n{outputFile}\r\n\r\n{msg}",
					"New Weblog Post", MessageBoxButton.OK, MessageBoxImage.Error);
				mmApp.Log($"New Weblog Post Creation Error\r\n{outputFile}", ex, false, LogLevels.Warning);
				return;
			}

			Model.Window.OpenTab(outputFile);
            Model.Window.ShowFolderBrowser(folder: mmPostFolder);

			mmApp.Configuration.LastFolder = mmPostFolder;
		}

		/// <summary>
		/// Generates a filename from a post title using snake case breaking
		/// </summary>
		/// <param name="title"></param>
		/// <returns></returns>
		public static string GetPostFileNameFromTitle(string title)
		{
			string titleFilename = FileUtils.SafeFilename(title);

			// Additional fix ups
			titleFilename = titleFilename
				.Replace(" ", "-")
				.Replace("--","-")
				.Replace("'", "")
				.Replace("`", "")
				.Replace("\"", "")
				.Replace("/","")
				.Replace("\\","")
				.Replace("&", "and");

			return titleFilename;
		}

		/// <summary>
		/// determines whether post is a new post based on
		/// a postId of various types
		/// </summary>
		/// <param name="postId">Integer or String or null</param>
		/// <returns></returns>
		bool IsNewPost(object postId)
		{
			if (postId == null)
				return true;

			if (postId is string)
				return string.IsNullOrEmpty((string) postId);

			if (postId is int && (int)postId < 1)
				return true;

			return false;
		}


#endregion



#region Downloaded Post Handling

		public void CreateDownloadedPostOnDisk(Post post, string weblogName)
		{
			string filename = FileUtils.SafeFilename(post.Title);

			var folder = Path.Combine(WeblogAddinConfiguration.Current.PostsFolder,
				"Downloaded",weblogName,
				filename);

			if (!Directory.Exists(folder))
				Directory.CreateDirectory(folder);
			var outputFile = Path.Combine(folder, StringUtils.ToCamelCase(filename) + ".md");



			bool isMarkdown = false;
			string body = post.Body;
			string featuredImage = null;

			if (post.CustomFields != null)
			{
				var cf = post.CustomFields.FirstOrDefault(custf => custf.Id == "mt_markdown");
				if (cf != null)
				{
					body = cf.Value;
					isMarkdown = true;
				}

				cf = post.CustomFields.FirstOrDefault(custf => custf.Id == "wp_post_thumbnail");
				if (cf != null)
					featuredImage = cf.Value;
			}
			if (!isMarkdown)
			{
				if (!string.IsNullOrEmpty(post.mt_text_more))
				{
					// Wordpress ReadMore syntax - SERIOUSLY???
					if (string.IsNullOrEmpty(post.mt_excerpt))
						post.mt_excerpt = HtmlUtils.StripHtml(post.Body);

					body = MarkdownUtilities.HtmlToMarkdown(body) +
							$"{mmApp.NewLine}{mmApp.NewLine}<!--more-->{mmApp.NewLine}{mmApp.NewLine}" +
							MarkdownUtilities.HtmlToMarkdown(post.mt_text_more);
				}
				else
					body = MarkdownUtilities.HtmlToMarkdown(body);

			}

			string categories = null;
			if (post.Categories != null && post.Categories.Length > 0)
				categories = string.Join(",", post.Categories);


			// Create the new post by creating a file with title preset
			var meta = new WeblogPostMetadata()
			{
				Title = post.Title,
				MarkdownBody = body,
				Categories = categories,
				Keywords = post.mt_keywords,
				Abstract = post.mt_excerpt,
				PostId = post.PostId.ToString(),
				WeblogName = weblogName,
				FeaturedImageUrl = featuredImage,
				PostDate = post.DateCreated,
				PostStatus = post.PostStatus,
				Permalink = post.Permalink
			};

			string newPostMarkdown = NewWeblogPost(meta);

			try
			{
				File.WriteAllText(outputFile, newPostMarkdown);
			}
			catch (Exception ex)
			{
				MessageBox.Show(WeblogForm, $@"Couldn't write new file at:
{outputFile}

{ex.Message}
",
					"Weblog Entry File not created",
					MessageBoxButton.OK,
					MessageBoxImage.Warning);
				return;
			}

			mmApp.Configuration.LastFolder = Path.GetDirectoryName(outputFile);

			if (isMarkdown)
			{
				string html = post.Body;
				string path = mmApp.Configuration.LastFolder;

				// do this synchronously so images show up :-<
				ShowStatus("Downloading post images...", mmApp.Configuration.StatusMessageTimeout);
				SaveMarkdownImages(html, path);
				ShowStatus("Post download complete.", mmApp.Configuration.StatusMessageTimeout);

				//new Action<string,string>(SaveImages).BeginInvoke(html,path,null, null);
			}

			Model.Window.OpenTab(outputFile);
            Model.Window.ShowFolderBrowser(folder: Path.GetDirectoryName(outputFile));
        }

		private void SaveMarkdownImages(string htmlText, string basePath)
		{
			try
			{
				var doc = new HtmlDocument();
				doc.LoadHtml(htmlText);

				// send up normalized path images as separate media items
				var images = doc.DocumentNode.SelectNodes("//img");
				if (images != null)
				{
					foreach (HtmlNode img in images)
					{
						string imgFile = img.Attributes["src"]?.Value;
						if (imgFile == null)
							continue;

						if (imgFile.StartsWith("http://") || imgFile.StartsWith("https://"))
						{
							string imageDownloadPath = Path.Combine(basePath, Path.GetFileName(imgFile));

							try
							{
								var http = new HttpUtilsWebClient();
								http.DownloadFile(imgFile, imageDownloadPath);
							}
							catch // just continue on errorrs
							{ }
						}
					}
				}
			}
			catch // catch so thread doesn't crash
			{
			}
		}

		#endregion


		#region Main Menu Pad for WebLog

		MenuItem MainMenuItem = null;

		void AddMainMenuItems()
		{
			// create commands
			Command_WeblogForm();
            Command_WebLogSearch();


			MainMenuItem = new MenuItem
			{
				Header = "Web_log"
			};

            AddMenuItem(MainMenuItem, "MainMenuTools", addMode: AddMenuItemModes.AddBefore);
			MainMenuItem.Items.Add(new Separator());

			MainMenuItem.SubmenuOpened += MainMenuItem_SubmenuOpened;
		}

		private void MainMenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
		{
			MainMenuItem.Items.Clear();

			var mi = new MenuItem
			{
				Header = "_Post to Weblog",
				Command = WeblogFormCommand,
				CommandParameter = "posttoweblog",
			};
			MainMenuItem.Items.Add(mi);

			mi = new MenuItem
			{
				Header = "_New Weblog Post",
				Command = WeblogFormCommand,
				CommandParameter = "newweblogpost"
			};
			MainMenuItem.Items.Add(mi);


			mi = new MenuItem
			{
				Header = "_Download Weblog Posts",
				Command = WeblogFormCommand,
				CommandParameter = "downloadweblogpost"
			};
			MainMenuItem.Items.Add(mi);

            MainMenuItem.Items.Add(new Separator());

			mi = new MenuItem
			{
				Header = "_Open Weblog Posts Folder",
				Command = WeblogFormCommand,
				CommandParameter = "openweblogfolder"
			};
			MainMenuItem.Items.Add(mi);

            mi = new MenuItem
            {
                Header = "_Search Weblog Posts Folder",
                Command = WebLogSearchCommand
            };
            MainMenuItem.Items.Add(mi);
            
			MainMenuItem.Items.Add(new Separator());

			var curText = Model.ActiveDocument?.CurrentText;
			if (!string.IsNullOrEmpty(curText) &&
				curText.Contains("permalink: ", StringComparison.InvariantCultureIgnoreCase))
			{
                mi = new MenuItem
				{
					Header = "Open Blog Post in _Browser",
					Command = WeblogFormCommand,
					CommandParameter = "openblogpost"
				};
				MainMenuItem.Items.Add(mi);
				MainMenuItem.Items.Add(new Separator());
			}

			mi = new MenuItem
			{
				Header = "_Configure Weblogs",
				Command = WeblogFormCommand,
				CommandParameter = "configureweblog"
			};
			MainMenuItem.Items.Add(mi);

			MainMenuItem.IsSubmenuOpen = true;
		}

		public CommandBase WeblogFormCommand { get; set; }

		void Command_WeblogForm()
		{
			WeblogFormCommand = new CommandBase((parameter, command) =>
			{
				var action = parameter as string;
				if (string.IsNullOrEmpty(action))
					return;

				if (action == "openweblogfolder")
				{
					ShellUtils.OpenFileInExplorer(WeblogModel.Configuration.PostsFolder);
					return;
				}
				else if (action == "openblogpost")
				{
					var link = StringUtils.ExtractString(Model.ActiveDocument?.CurrentText, "\npermalink: ", "\n",
						true);
					if (!string.IsNullOrEmpty(link))
						mmFileUtils.ShowExternalBrowser(link);
					return;
				}

				// actions that require form to be open
				var form = new WeblogForm(WeblogModel)
				{
					Owner = Model.Window
				};
				form.Model.AppModel = Model;
				form.Show();

				switch (action)
				{
					case "posttoweblog":
						form.TabControl.SelectedIndex = 0;
						break;
					case "newweblogpost":
						form.TabControl.SelectedIndex = 1;
						break;
					case "downloadweblogpost":
						form.TabControl.SelectedIndex = 2;
						break;
					case "configureweblog":
						form.TabControl.SelectedIndex = 3;
						break;
				}
			}, (p, c) =>
			{
				var action = p as string;
				if (string.IsNullOrEmpty(action))
					return true;

				if (action == "posttoweblog")
					return Model.ActiveEditor != null;

				return true;
			});
		}


        public CommandBase WebLogSearchCommand { get; set; }

        void Command_WebLogSearch()
        {
            WebLogSearchCommand = new CommandBase((parameter, command) =>
            {
                var searchControl = Model.Window.OpenSearchPane();
                searchControl.Model.SearchFolder = WeblogModel.Configuration.PostsFolder;
                searchControl.Model.FileFilters = "*.md,*.markdown";
            }, (p, c) => true);
        }


        #endregion
    }



}
