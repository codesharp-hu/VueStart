using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BootGen;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;

namespace VueStart.Services
{
    public class GenerationService
    {
        private readonly IMemoryCache memoryCache;

        public GenerationService(IMemoryCache memoryCache)
        {
            this.memoryCache = memoryCache;
        }
        public string Generate(JsonElement json, string title, string templateFileName, out string appjs, out string indexhtml) {
            var dataModel = new DataModel {
              TypeToString = TypeScriptGenerator.ToTypeScriptType
            };
            var jObject = JObject.Parse(json.ToString(), new JsonLoadSettings { CommentHandling = CommentHandling.Ignore, DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            dataModel.LoadRootObject("App", jObject);
            var collection = new ResourceCollection(dataModel);
            var seedStore = new SeedDataStore(collection);
            seedStore.Load(jObject);

            var id = Guid.NewGuid().ToString();
            var generator = new TypeScriptGenerator(null);
            generator.Templates = Load("templates");
            appjs = generator.Render(templateFileName, new Dictionary<string, object> {
                {"classes", dataModel.CommonClasses}
            });
            indexhtml = generator.Render("index.sbn", new Dictionary<string, object> {
                {"base_url", $"/api/files/{id}/"},
                {"title", $"{title}"}
            });
            return id;
        }

        public JsonElement Fix(JsonElement json) {
            var jObject = JObject.Parse(json.ToString(), new JsonLoadSettings { CommentHandling = CommentHandling.Ignore, DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            try {
                var dataModel = new DataModel {
                TypeToString = TypeScriptGenerator.ToTypeScriptType
                };
                dataModel.LoadRootObject("App", jObject);
            } catch (NamingException e) {
                var jsonString = jObject.RenamingArrays(e.ActualName, e.SuggestedName).ToString();
                return JsonDocument.Parse(jsonString).RootElement;
            }
            return json;
        }

        public string GenerateToCache(JsonElement json, string title, string templateFileName) {
            string id = Generate(json, title, templateFileName, out string appjs, out string indexhtml);
            memoryCache.Set($"{id}/app.js", Minify(appjs), TimeSpan.FromMinutes(3));
            memoryCache.Set($"{id}/index.html", Minify(indexhtml), TimeSpan.FromMinutes(3));
            return id;
        }

        private string Minify(string value) {
            value = value.Replace("\n", " ");
            value = value.Replace("\r", " ");
            value = value.Replace("\t", " ");
            int length;
            do {
                length = value.Length;
                value = value.Replace("  ", " ");
            } while(value.Length != length);

            return value;
        }
        private struct TemplateCacheKey
        {
            public string Path { get; init; }
        }

        private VirtualDisk Load(string path)
        {
            return memoryCache.GetOrCreate(new TemplateCacheKey { Path = path }, entry =>{
                var templates = new VirtualDisk();
                foreach (var file in Directory.EnumerateFiles(path))
                {
                    templates.Files.Add(new VirtualFile
                    {
                        Name = Path.GetFileName(file),
                        Path = "",
                        Content = System.IO.File.ReadAllText(file)
                    });
                }
                return templates;
            });
        }
    }
}