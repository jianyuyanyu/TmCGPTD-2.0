﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static TmCGPTD.Models.PostageSqlModels;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Reactive.Joins;
using FluentAvalonia.UI.Controls;
using Postgrest;
using static Postgrest.QueryOptions;
using System.Reflection;
using static Postgrest.Constants;
using TmCGPTD.ViewModels;
using TmCGPTD.Views;
using System.Transactions;
using System.Security.Cryptography;

namespace TmCGPTD.Models
{
    public class SyncProcess
    {
        // -------------------------------------------------------------------------------------------------------

        public async Task SyncDbAsync()
        {
            try
            {
                if (VMLocator.MainViewModel._supabase == null)
                {
                    throw new Exception($"Supabase initialization failed.");
                }

                if (VMLocator.MainViewModel._supabase.Auth.CurrentUser == null)
                {
                    throw new Exception($"Failed to obtain Supabase user ID.");
                }
            }
            catch (Exception ex)
            {
                var cdialog = new ContentDialog
                {
                    Title = "Error",
                    Content = ex.Message,
                    CloseButtonText = "OK"
                };
                await VMLocator.MainViewModel.ContentDialogShowAsync(cdialog);
                return;
            }

            var supabase = VMLocator.MainViewModel._supabase;
            var uid = VMLocator.MainViewModel._supabase.Auth.CurrentUser.Id;

            int cloudIsNewer = 0;
            int localIsNewer = 0;
            int localOnly = 0;
            int cloudRecords = 0;
            int localRecords = 0;

            try
            {
                var resultPhrase = await supabase
                                  .From<Phrase>()
                                  .Select(x => new object[] { x.Id, x.Date })
                                  .Order(x => x.Id, Ordering.Descending)
                                  .Get();

                cloudRecords = cloudRecords + resultPhrase.Models.Count;

                using SQLiteConnection connection = new SQLiteConnection($"Data Source={AppSettings.Instance.DbPath}");
                await connection.OpenAsync();

                string sql = "SELECT id, date FROM phrase ORDER BY id DESC";
                using var command = new SQLiteCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    //resultに同一のIDがあるか検索する
                    var target = resultPhrase.Models.FirstOrDefault(x => x.Id == (long)reader["id"]);
                    if (target != null)
                    {
                        if (reader["date"] != DBNull.Value)
                        {
                            if (target.Date > (DateTime)reader["date"]) //クラウドが新しい
                            {
                                cloudIsNewer++;
                            }
                            else if (target.Date < (DateTime)reader["date"]) //ローカルが新しい
                            {
                                localIsNewer++;
                            }
                        }
                        else //ローカルに日付がない
                        {
                            cloudIsNewer++;
                        }
                    }
                    else //クラウドに該当IDがない
                    {
                        localOnly++;
                    }
                    localRecords++;
                }

                var resultTemplate = await supabase
                                        .From<Template>()
                                        .Select(x => new object[] { x.Id, x.Date })
                                        .Order(x => x.Id, Ordering.Descending)
                                        .Get();

                cloudRecords = cloudRecords + resultTemplate.Models.Count;

                sql = "SELECT id, date FROM template ORDER BY id DESC";
                using var command2 = new SQLiteCommand(sql, connection);
                using var reader2 = await command2.ExecuteReaderAsync();

                while (await reader2.ReadAsync())
                {
                    var target = resultTemplate.Models.FirstOrDefault(x => x.Id == (long)reader2["id"]);
                    if (target != null)
                    {
                        if (reader2["date"] != DBNull.Value)
                        {
                            if (target.Date > (DateTime)reader2["date"])
                            {
                                cloudIsNewer++;
                            }
                            else if (target.Date < (DateTime)reader2["date"])
                            {
                                localIsNewer++;
                            }
                        }
                        else
                        {
                            cloudIsNewer++;
                        }
                    }
                    else
                    {
                        localOnly++;
                    }
                    localRecords++;
                }

                var resultEditorLog = await supabase
                                        .From<EditorLog>()
                                        .Select(x => new object[] { x.Id, x.Date })
                                        .Order(x => x.Id, Ordering.Descending)
                                        .Get();

                cloudRecords = cloudRecords + resultEditorLog.Models.Count;

                sql = "SELECT id, date FROM editorlog ORDER BY id DESC";
                using var command3 = new SQLiteCommand(sql, connection);
                using var reader3 = await command3.ExecuteReaderAsync();

                while (await reader3.ReadAsync())
                {
                    var target = resultEditorLog.Models.FirstOrDefault(x => x.Id == (long)reader3["id"]);
                    if (target != null)
                    {
                        if (reader3["date"] != DBNull.Value)
                        {
                            if (target.Date > (DateTime)reader3["date"])
                            {
                                cloudIsNewer++;
                            }
                            else if (target.Date < (DateTime)reader3["date"])
                            {
                                localIsNewer++;
                            }
                        }
                        else
                        {
                            cloudIsNewer++;
                        }
                    }
                    else
                    {
                        localOnly++;
                    }
                    localRecords++;
                }

                var resultChatRoom = await supabase
                                        .From<ChatRoom>()
                                        .Select(x => new object[] { x.Id, x.UpdatedOn })
                                        .Order(x => x.Id, Ordering.Descending)
                                        .Get();

                cloudRecords = cloudRecords + resultChatRoom.Models.Count;

                sql = "SELECT id, date FROM chatlog ORDER BY id DESC";
                using var command4 = new SQLiteCommand(sql, connection);
                using var reader4 = await command4.ExecuteReaderAsync();

                while (await reader4.ReadAsync())
                {
                    var target = resultChatRoom.Models.FirstOrDefault(x => x.Id == (long)reader4["id"]);
                    if (target != null)
                    {
                        if (reader4["date"] != DBNull.Value)
                        {
                            if (target.UpdatedOn > (DateTime)reader4["date"])
                            {
                                cloudIsNewer++;
                            }
                            else if (target.UpdatedOn < (DateTime)reader4["date"])
                            {
                                localIsNewer++;
                            }
                        }
                        else
                        {
                            cloudIsNewer++;
                        }
                    }
                    else
                    {
                        localOnly++;
                    }
                    localRecords++;
                }

                if (localOnly > 0 && cloudIsNewer == 0 && localIsNewer == 0 && cloudRecords == 0)
                {
                    //クラウドにデータが無ければ、ローカルの全データをコピーして初回同期する
                    //コピー後にローカルを全削除してクラウドからフェッチ
                    await CopyAllLocalToCloudDbAsync();
                    Debug.WriteLine("Uploaded sync status from local. localOnly: " + localOnly);
                }
                else if (cloudIsNewer > 0 && localIsNewer == 0 && localOnly == 0)
                {
                    //クラウドのデータをローカルにコピー
                    await UpsertToLocalDbAsync();
                    Debug.WriteLine("Sync state fetched from the cloud. cloudIsNewer: " + cloudIsNewer);
                }
                else if (localIsNewer > 0 && cloudIsNewer == 0 && localOnly >= 0)
                {
                    //ローカルのデータをコピー
                    await UpsertToCloudDbAsync();
                    Debug.WriteLine("Uploaded sync status from local. localIsNewer: " + localIsNewer);
                }
                else if ((localOnly == 0 && cloudIsNewer == 0 && localIsNewer == 0 && localRecords == cloudRecords) || (localRecords == 0 && cloudRecords == 0))
                {
                    //同期済みまたは初期値
                    Debug.WriteLine("Sync status is up-to-date.");
                }
                else
                {
                    //競合が発生
                    var cdialog = new ContentDialog
                    {
                        Title = "Data conflicts.",
                        Content = $"Please merge or select preferred data.\n*Note that data may be lost if you choose Cloud or Local.\nThis warning appears when data is deleted on the cloud side.\n\nCloud records: {cloudRecords},  Local records: {localRecords}\nCloud is newer: {cloudIsNewer},  Local is newer: {localIsNewer},  Local only: {localOnly}",
                        PrimaryButtonText = "Merge",
                        SecondaryButtonText = "Cloud",
                        CloseButtonText = "Local",
                        DefaultButton = ContentDialogButton.Primary
                    };

                    var result = await VMLocator.MainViewModel.ContentDialogShowAsync(cdialog);
                    if (result == ContentDialogResult.Primary) //マージ
                    {
                        await UpsertToCloudDbAsync();
                        await UpsertToLocalDbAsync();
                        Debug.WriteLine("Merge");
                    }
                    else if (result == ContentDialogResult.Secondary) //クラウドを優先
                    {

                        Debug.WriteLine("Cloud");
                    }
                    else if (result == ContentDialogResult.None) //ローカルを優先
                    {
                        Debug.WriteLine("Local");
                    }

                }
            }
            catch (Exception ex)
            {
                var cdialog = new ContentDialog
                {
                    Title = "Error",
                    Content = ex.Message + ex.StackTrace,
                    CloseButtonText = "OK"
                };
                await VMLocator.MainViewModel.ContentDialogShowAsync(cdialog);
            }
        }
        // -------------------------------------------------------------------------------------------------------
        // ローカルデータを更新した時はこのメソッドを直接呼び出してから、その後にSyncDbAsync()でデータの整合性をチェックする
        public async Task UpsertToCloudDbAsync()
        {
            var _dbProcess = DatabaseProcess.Instance;
            var supabase = VMLocator.MainViewModel._supabase;
            var uid = VMLocator.MainViewModel._supabase!.Auth.CurrentUser!.Id;

            using SQLiteConnection connection = new SQLiteConnection($"Data Source={AppSettings.Instance.DbPath}");
            await connection.OpenAsync();

            try
            {
                var resultPhrase = await supabase!
                      .From<Phrase>()
                      .Order(x => x.Id, Ordering.Descending)
                      .Get();

                var models = new List<Phrase>();

                string sql = "SELECT * FROM phrase ORDER BY name COLLATE NOCASE";
                using var command2 = new SQLiteCommand(sql, connection);
                using var reader = await command2.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var target = resultPhrase.Models.FirstOrDefault(x => x.Id == (long)reader["id"]); 

                    if (target == null || (reader["date"] != DBNull.Value && target.Date < (DateTime)reader["date"])) // クラウドにデータが無いか、ローカルの日付が新しい場合
                    {
                        models.Add(new Phrase { UserId = uid, Name = (string)reader["name"], Content = (string)reader["phrase"], Date = (DateTime)reader["date"] });
                    }
                    else if (reader["date"] == DBNull.Value) // ローカルの日付がNullの場合
                    {
                        models.Add(new Phrase { UserId = uid, Name = (string)reader["name"], Content = (string)reader["phrase"], Date = DateTime.Now });
                    }
                }

                await supabase!.From<Phrase>().Upsert(models);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to upload phrase presets: {ex.Message}\n{ex.StackTrace}");
            }


            try
            {
                var resultEditorLog = await supabase
                        .From<EditorLog>()
                        .Order(x => x.Id, Ordering.Descending)
                        .Get();

                var models = new List<EditorLog>();

                string sql = "SELECT * FROM editorlog ORDER BY date ASC LIMIT 200";
                using var command2 = new SQLiteCommand(sql, connection);
                using var reader = await command2.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var target = resultEditorLog.Models.FirstOrDefault(x => x.Id == (long)reader["id"]);

                    if (target == null || (reader["date"] != DBNull.Value && target.Date < (DateTime)reader["date"])) // クラウドにデータが無いか、ローカルの日付が新しい場合
                    {
                        models.Add(new EditorLog { UserId = uid, Content = (string)reader["text"], Date = (DateTime)reader["date"] });
                    }
                    else if (reader["date"] == DBNull.Value) // ローカルの日付がNullの場合
                    {
                        models.Add(new EditorLog { UserId = uid, Content = (string)reader["text"], Date = DateTime.Now });
                    }
                }

                await supabase.From<EditorLog>().Upsert(models);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to upload editor logs: {ex.Message}\n{ex.StackTrace}");
            }


            try
            {
                var resultTemplate = await supabase
                        .From<Template>()
                        .Order(x => x.Id, Ordering.Descending)
                        .Get();

                var models = new List<Template>();

                string sql = "SELECT * FROM template ORDER BY title ASC";
                using var command2 = new SQLiteCommand(sql, connection);
                using var reader = await command2.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var target = resultTemplate.Models.FirstOrDefault(x => x.Id == (long)reader["id"]);

                    if (target == null || (reader["date"] != DBNull.Value && target.Date < (DateTime)reader["date"])) // クラウドにデータが無いか、ローカルの日付が新しい場合
                    {
                        models.Add(new Template { UserId = uid, Title = (string)reader["title"], Content = (string)reader["text"], Date = (DateTime)reader["date"] });
                    }
                    else if (reader["date"] == DBNull.Value) // ローカルの日付がNullの場合
                    {
                        models.Add(new Template { UserId = uid, Title = (string)reader["title"], Content = (string)reader["text"], Date = DateTime.Now });
                    }
                }

                await supabase.From<Template>().Insert(models);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to upload templates: {ex.Message}\n{ex.StackTrace}");
            }


            try
            {
                var resultChatRoom = await supabase
                        .From<ChatRoom>()
                        .Order(x => x.Id, Ordering.Descending)
                        .Get();

                var models2 = new List<Message>();

                string sql = "SELECT * FROM chatlog ORDER BY date ASC";
                using var command2 = new SQLiteCommand(sql, connection);
                using var reader = await command2.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var target = resultChatRoom.Models.FirstOrDefault(x => x.Id == (long)reader["id"]);

                    var models1 = new List<ChatRoom>();
                    if (target == null || (reader["date"] != DBNull.Value && target.UpdatedOn < (DateTime)reader["date"])) // クラウドにデータが無いか、ローカルの日付が新しい場合
                    {
                        models1.Add(new ChatRoom { UserId = uid, Title = (string)reader["title"], Category = (string)reader["category"], LastPrompt = (string)reader["lastprompt"], Json = (string)reader["json"], JsonPrev = (string)reader["jsonprev"], UpdatedOn = (DateTime)reader["date"] });

                        var returnValue = await supabase.From<ChatRoom>().Upsert(models1, new QueryOptions { Returning = ReturnType.Representation });

                        var roomId = returnValue.Models[0].Id;

                        models2.AddRange(DivideMessage((string)reader["text"], roomId, uid!)); // メッセージを分割して追加
                    }
                    else if (reader["date"] == DBNull.Value) // ローカルの日付がNullの場合
                    {
                        models1.Add(new ChatRoom { UserId = uid, Title = (string)reader["title"], Category = (string)reader["category"], LastPrompt = (string)reader["lastprompt"], Json = (string)reader["json"], JsonPrev = (string)reader["jsonprev"], UpdatedOn = DateTime.Now });

                        var returnValue = await supabase.From<ChatRoom>().Upsert(models1, new QueryOptions { Returning = ReturnType.Representation });

                        var roomId = returnValue.Models[0].Id;

                        models2.AddRange(DivideMessage((string)reader["text"], roomId, uid!));
                    }
                }

                await supabase.From<Message>().Upsert(models2);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to upload chat logs: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // -------------------------------------------------------------------------------------------------------

        public async Task UpsertToLocalDbAsync()
        {
            var _dbProcess = DatabaseProcess.Instance;
            var supabase = VMLocator.MainViewModel._supabase;

            using SQLiteConnection connection = new SQLiteConnection($"Data Source={AppSettings.Instance.DbPath}");
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            {
                try
                {
                    var resultPhrase = await supabase!
                          .From<Phrase>()
                          .Order(x => x.Id, Ordering.Descending)
                          .Get();

                    Dictionary<long, DateTime?> localData = new Dictionary<long, DateTime?>();

                    using var command = new SQLiteCommand("SELECT id, date FROM phrase ORDER BY id DESC", connection);
                    using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        long id = (long)reader["id"];
                        DateTime? date = reader["date"] == DBNull.Value ? null : (DateTime)reader["date"];
                        localData[id] = date;
                    }

                    foreach (var cloudData in resultPhrase.Models)
                    {
                        // クラウドの日付が新しいか、ローカルの日付がNull、またはローカルにデータが存在しない場合
                        if (!localData.TryGetValue(cloudData.Id, out DateTime? localDate) || localDate == null || cloudData.Date > localDate)
                        {
                            var updateSql = @"INSERT INTO phrase (id, name, phrase, date) VALUES (@Id, @Name, @Content, @Date) 
                                              ON CONFLICT(id) DO UPDATE SET name = excluded.name, phrase = excluded.phrase, date = excluded.date;";
                            var updateCommand = new SQLiteCommand(updateSql, connection);
                            updateCommand.Parameters.AddWithValue("@Id", cloudData.Id);
                            updateCommand.Parameters.AddWithValue("@Name", cloudData.Name);
                            updateCommand.Parameters.AddWithValue("@Content", cloudData.Content);
                            updateCommand.Parameters.AddWithValue("@Date", cloudData.Date);

                            await updateCommand.ExecuteNonQueryAsync();
                        }
                    }

                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    throw new Exception($"Failed to download phrase presets: {ex.Message}\n{ex.StackTrace}");
                }
            }

            using var transaction2 = connection.BeginTransaction();
            {
                try
                {
                    var resultTemplate = await supabase
                                    .From<Template>()
                                    .Order(x => x.Id, Ordering.Descending)
                                    .Get();

                    Dictionary<long, DateTime?> localData = new Dictionary<long, DateTime?>();

                    string sql = "SELECT id, date FROM template ORDER BY id DESC";
                    using var command2 = new SQLiteCommand(sql, connection);
                    using var reader = await command2.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        long id = (long)reader["id"];
                        DateTime? date = reader["date"] == DBNull.Value ? null : (DateTime)reader["date"];
                        localData[id] = date;
                    }

                    foreach (var cloudData in resultTemplate.Models)
                    {
                        // クラウドの日付が新しいか、ローカルの日付がNull、またはローカルにデータが存在しない場合
                        if (!localData.TryGetValue(cloudData.Id, out DateTime? localDate) || localDate == null || cloudData.Date > localDate)
                        { 
                            var updateSql = @"INSERT INTO template (id, title, text, date) VALUES (@Id, @Name, @Content, @Date) 
                                              ON CONFLICT(id) DO UPDATE SET title = excluded.title, text = excluded.text, date = excluded.date;";
                            var command = new SQLiteCommand(updateSql, connection);
                            command.Parameters.AddWithValue("@Id", cloudData.Id);
                            command.Parameters.AddWithValue("@Name", cloudData.Title);
                            command.Parameters.AddWithValue("@Content", cloudData.Content);
                            command.Parameters.AddWithValue("@Date", cloudData.Date);

                                await command.ExecuteNonQueryAsync();
                        }
                    }

                    transaction2.Commit();
                }
                catch (Exception ex)
                {
                    transaction2.Rollback();
                    throw new Exception($"Failed to download template presets: {ex.Message}\n{ex.StackTrace}");
                }
            }

            using var transaction3 = connection.BeginTransaction();
            {
                try
                {
                    var resultEditorLog = await supabase
                                    .From<EditorLog>()
                                    .Order(x => x.Id, Ordering.Descending)
                                    .Get();

                    Dictionary<long, DateTime?> localData = new Dictionary<long, DateTime?>();

                    string sql = "SELECT id, date FROM editorlog ORDER BY id DESC";
                    using var command3 = new SQLiteCommand(sql, connection);
                    using var reader = await command3.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        long id = (long)reader["id"];
                        DateTime? date = reader["date"] == DBNull.Value ? null : (DateTime)reader["date"];
                        localData[id] = date;
                    }

                    foreach (var cloudData in resultEditorLog.Models)
                    {
                        // クラウドの日付が新しいか、ローカルの日付がNull、またはローカルにデータが存在しない場合
                        if (!localData.TryGetValue(cloudData.Id, out DateTime? localDate) || localDate == null || cloudData.Date > localDate)
                        {
                            var updateSql = @"INSERT INTO editorlog (id, date, text) VALUES (@Id, @Date, @Content) 
                                              ON CONFLICT(id) DO UPDATE SET date = excluded.date, text = excluded.text;";
                            var command = new SQLiteCommand(updateSql, connection);
                            command.Parameters.AddWithValue("@Id", cloudData.Id);
                            command.Parameters.AddWithValue("@Date", cloudData.Date);
                            command.Parameters.AddWithValue("@Content", cloudData.Content);
                        }
                    }

                    transaction3.Commit();
                }
                catch (Exception ex)
                {
                    transaction3.Rollback();
                    throw new Exception($"Failed to download editor logs: {ex.Message}\n{ex.StackTrace}");
                }
            }

            using var transaction4 = connection.BeginTransaction();
            {
                try
                {
                    var resultChatRoom = await supabase
                                    .From<ChatRoom>()
                                    .Order(x => x.Id, Ordering.Descending)
                                    .Get();

                    Dictionary<long, DateTime?> localData = new Dictionary<long, DateTime?>();

                    string sql = "SELECT * FROM chatlog ORDER BY id DESC";
                    using var command4 = new SQLiteCommand(sql, connection);
                    using var reader = await command4.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        long id = (long)reader["id"];
                        DateTime? date = reader["date"] == DBNull.Value ? null : (DateTime)reader["date"];
                        localData[id] = date;
                    }

                    foreach (var cloudData in resultChatRoom.Models)
                    {
                        // クラウドの日付が新しいか、ローカルの日付がNull、またはローカルにデータが存在しない場合
                        if (!localData.TryGetValue(cloudData.Id, out DateTime? localDate) || localDate == null || cloudData.UpdatedOn > localDate)
                        {
                            var resultMessage = await supabase.From<Message>().Where(x => x.RoomId == cloudData.Id).Order(x => x.Id, Ordering.Ascending).Get();

                            string combinedMessage = CombineMessage(resultMessage.Models);

                            var updateSql = @"INSERT INTO chatlog (id, date, title, json, text, category, lastprompt, jsonprev) VALUES (@Id, @UpdatedOn, @Title, @Json, @Message, @Category, @LastPrompt, @JsonPrev) 
                                ON CONFLICT(id) DO UPDATE SET date = excluded.date, title = excluded.title, json = excluded.json, text = excluded.text, category = excluded.category, lastprompt = excluded.lastprompt, jsonprev = excluded.jsonprev;";
                            var command = new SQLiteCommand(updateSql, connection);
                            command.Parameters.AddWithValue("@Id", cloudData.Id);
                            command.Parameters.AddWithValue("@UpdatedOn", cloudData.UpdatedOn);
                            command.Parameters.AddWithValue("@Title", cloudData.Title);
                            command.Parameters.AddWithValue("@Json", cloudData.Json);
                            command.Parameters.AddWithValue("@Message", combinedMessage);
                            command.Parameters.AddWithValue("@Category", cloudData.Category);
                            command.Parameters.AddWithValue("@LastPrompt", cloudData.LastPrompt);
                            command.Parameters.AddWithValue("@JsonPrev", cloudData.JsonPrev);

                            await command.ExecuteNonQueryAsync();
                        }
                    }

                    transaction4.Commit();
                }
                catch (Exception ex)
                {
                    transaction4.Rollback();
                    throw new Exception($"Failed to download chat logs: {ex.Message}\n{ex.StackTrace}");
                }

                // インメモリをいったん閉じてまた開く
                await DatabaseProcess.memoryConnection!.CloseAsync();
                await _dbProcess.DbLoadToMemoryAsync();
                VMLocator.DataGridViewModel.DataGridIsFocused = false;
                VMLocator.DataGridViewModel.ChatList = await _dbProcess.SearchChatDatabaseAsync();
                VMLocator.DataGridViewModel.SelectedItemIndex = -1;
                await _dbProcess.GetEditorLogDatabaseAsync();
                await _dbProcess.GetTemplateItemsAsync();
                await VMLocator.MainViewModel.LoadPhraseItemsAsync();
                VMLocator.EditorViewModel.SelectedEditorLogIndex = -1;
                VMLocator.EditorViewModel.SelectedTemplateItemIndex = -1;
            }
        }

        // -------------------------------------------------------------------------------------------------------
        public async Task CopyAllLocalToCloudDbAsync()
        {
            var _dbProcess = DatabaseProcess.Instance;
            string sourceFile = AppSettings.Instance.DbPath;
            string backupFile = AppSettings.Instance.DbPath + ".backup-" + DateTime.Now.ToString("yyyyMMddHHmmss");

            // Ensure the target does not exist.
            if (File.Exists(backupFile))
            {
                File.Delete(backupFile);
            }

            // Copy the file.
            File.Copy(sourceFile, backupFile);

            // Open SQLiteConnection.
            using SQLiteConnection connection = new SQLiteConnection($"Data Source={AppSettings.Instance.DbPath}");
            await connection.OpenAsync();

            var supabase = VMLocator.MainViewModel._supabase;
            var uid = VMLocator.MainViewModel._supabase!.Auth.CurrentUser!.Id;

            var cdialog = new ContentDialog() { Title = $"Synchronizing..." };
            cdialog.Content = new ProgressView()
            {
                DataContext = VMLocator.ProgressViewModel
            };
            VMLocator.ProgressViewModel.SetDialog(cdialog);
            await VMLocator.MainViewModel.ContentDialogShowAsync(cdialog);

            try
            {
                var models = new List<Phrase>();

                string sql = "SELECT COUNT(*) FROM phrase";
                using var command = new SQLiteCommand(sql, connection);
                long? countTable = (long?)await command.ExecuteScalarAsync();
                VMLocator.ProgressViewModel.ProgressText = $"Uploading phrase presets...";

                sql = "SELECT * FROM phrase ORDER BY name COLLATE NOCASE";
                using var command2 = new SQLiteCommand(sql, connection);

                using var reader = await command2.ExecuteReaderAsync();

                int i = 0;
                while (await reader.ReadAsync())
                {
                    i++;
                    VMLocator.ProgressViewModel.ProgressValue = ((double)i / countTable);
                    if (reader["date"] == DBNull.Value)
                    {
                        models.Add(new Phrase { UserId = uid, Name = (string)reader["name"], Content = (string)reader["phrase"], Date = DateTime.Now });
                    }
                    else
                    {
                        models.Add(new Phrase { UserId = uid, Name = (string)reader["name"], Content = (string)reader["phrase"], Date = (DateTime)reader["date"] });
                    }
                    await Task.Delay(10);
                }

                await supabase!.From<Phrase>().Insert(models);
            }
            catch (Exception ex)
            {
                VMLocator.ProgressViewModel.Hide();
                throw new Exception($"Failed to upload phrase presets: {ex.Message}\n{ex.StackTrace}");
            }


            try
            {
                var models = new List<EditorLog>();

                string sql = "SELECT COUNT(*) FROM editorlog LIMIT 200";
                using var command = new SQLiteCommand(sql, connection);
                long? countTable = (long?)await command.ExecuteScalarAsync();
                VMLocator.ProgressViewModel.ProgressText = $"Uploading editor-logs...";

                sql = "SELECT * FROM editorlog ORDER BY date ASC LIMIT 200";
                using var command2 = new SQLiteCommand(sql, connection);

                using var reader = await command2.ExecuteReaderAsync();

                int i = 0;
                while (await reader.ReadAsync())
                {
                    i++;
                    VMLocator.ProgressViewModel.ProgressValue = ((double)i / countTable);
                    if (reader["date"] == DBNull.Value)
                    {
                        models.Add(new EditorLog { UserId = uid, Content = (string)reader["text"], Date = DateTime.Now });
                    }
                    else
                    {
                        models.Add(new EditorLog { UserId = uid, Content = (string)reader["text"], Date = (DateTime)reader["date"] });
                    }
                    await Task.Delay(10);
                }

                await supabase.From<EditorLog>().Insert(models);
            }
            catch (Exception ex)
            {
                VMLocator.ProgressViewModel.Hide();
                throw new Exception($"Failed to upload editor logs: {ex.Message}\n{ex.StackTrace}");
            }


            try
            {
                var models = new List<Template>();

                string sql = "SELECT COUNT(*) FROM template";
                using var command = new SQLiteCommand(sql, connection);
                long? countTable = (long?)await command.ExecuteScalarAsync();
                VMLocator.ProgressViewModel.ProgressText = $"Uploading templates...";

                sql = "SELECT * FROM template ORDER BY title ASC";
                using var command2 = new SQLiteCommand(sql, connection);

                using var reader = await command2.ExecuteReaderAsync();

                int i = 0;
                while (await reader.ReadAsync())
                {
                    i++;
                    VMLocator.ProgressViewModel.ProgressValue = ((double)i / countTable);
                    if (reader["date"] == DBNull.Value)
                    {
                        models.Add(new Template { UserId = uid, Title = (string)reader["title"], Content = (string)reader["text"], Date = DateTime.Now });
                    }
                    else
                    {
                        models.Add(new Template { UserId = uid, Title = (string)reader["title"], Content = (string)reader["text"], Date = (DateTime)reader["date"] });
                    }
                    await Task.Delay(10);
                }

                await supabase.From<Template>().Insert(models);
            }
            catch (Exception ex)
            {
                VMLocator.ProgressViewModel.Hide();
                throw new Exception($"Failed to upload templates: {ex.Message}\n{ex.StackTrace}");
            }


            try
            {
                var models2 = new List<Message>();

                string sql = "SELECT COUNT(*) FROM chatlog";
                using var command = new SQLiteCommand(sql, connection);
                long? countTable = (long?)await command.ExecuteScalarAsync();
                VMLocator.ProgressViewModel.ProgressText = $"Uploading chat-logs...";

                sql = "SELECT * FROM chatlog ORDER BY date ASC";
                using var command2 = new SQLiteCommand(sql, connection);

                using var reader = await command2.ExecuteReaderAsync();

                int j = 0;
                while (await reader.ReadAsync())
                {
                    j++;
                    VMLocator.ProgressViewModel.ProgressValue = ((double)j / countTable);
                    var models1 = new List<ChatRoom>();

                    if (reader["date"] == DBNull.Value)
                    {
                        models1.Add(new ChatRoom { UserId = uid, Title = (string)reader["title"], Category = (string)reader["category"], LastPrompt = (string)reader["lastprompt"], Json = (string)reader["json"], JsonPrev = (string)reader["jsonprev"], UpdatedOn = DateTime.Now });
                    }
                    else
                    {
                        models1.Add(new ChatRoom { UserId = uid, Title = (string)reader["title"], Category = (string)reader["category"], LastPrompt = (string)reader["lastprompt"], Json = (string)reader["json"], JsonPrev = (string)reader["jsonprev"], UpdatedOn = (DateTime)reader["date"] });
                    }

                    var returnValue = await supabase.From<ChatRoom>().Insert(models1, new QueryOptions { Returning = ReturnType.Representation });

                    var roomId = returnValue.Models[0].Id;

                    models2.AddRange(DivideMessage((string)reader["text"], roomId, uid!));

                    await Task.Delay(10);
                }

                await supabase.From<Message>().Insert(models2);
            }
            catch (Exception ex)
            {
                VMLocator.ProgressViewModel.Hide();
                throw new Exception($"Failed to upload chat logs: {ex.Message}\n{ex.StackTrace}");
            }


            VMLocator.ProgressViewModel.ProgressText = $"Deleting local logs...";
            VMLocator.ProgressViewModel.ProgressValue = 0;
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    var tables = new List<string> { "phrase", "chatlog", "editorlog", "template" };
                    foreach (var table in tables)
                    {
                        var command = new SQLiteCommand($"DELETE FROM {table}", connection);
                        await command.ExecuteNonQueryAsync();
                    }
                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    VMLocator.ProgressViewModel.Hide();
                    throw new Exception($"Failed to delete local logs: {ex.Message} {ex.StackTrace}");
                }
            }

            VMLocator.ProgressViewModel.ProgressText = $"Fetching data from the cloud...";

            var resultPhrase = await supabase.From<Phrase>().Get();

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    foreach (var phrase in resultPhrase.Models)
                    {
                        var command = new SQLiteCommand("INSERT INTO phrase (id, name, phrase, date) VALUES (@Id, @Name, @Content, @Date)", connection);
                        command.Parameters.AddWithValue("@Id", phrase.Id);
                        command.Parameters.AddWithValue("@Name", phrase.Name);
                        command.Parameters.AddWithValue("@Content", phrase.Content);
                        command.Parameters.AddWithValue("@Date", phrase.Date);

                        await command.ExecuteNonQueryAsync();
                    }
                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    VMLocator.ProgressViewModel.Hide();
                    throw new Exception($"Failed to download phrases: {ex.Message} {ex.StackTrace}");
                }
            }

            VMLocator.ProgressViewModel.ProgressValue = 0.25;

            var resultEditorLog = await supabase.From<EditorLog>().Get();
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    foreach (var editorLog in resultEditorLog.Models)
                    {
                        var command = new SQLiteCommand("INSERT INTO editorlog (id, date, text) VALUES (@Id, @Date, @Content )", connection);
                        command.Parameters.AddWithValue("@Id", editorLog.Id);
                        command.Parameters.AddWithValue("@Date", editorLog.Date);
                        command.Parameters.AddWithValue("@Content", editorLog.Content);

                        await command.ExecuteNonQueryAsync();
                    }
                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    VMLocator.ProgressViewModel.Hide();
                    throw new Exception($"Failed to download editor logs: {ex.Message} {ex.StackTrace}");
                }
            }

            VMLocator.ProgressViewModel.ProgressValue = 0.5;

            var resultTemplate = await supabase.From<Template>().Get();
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    foreach (var template in resultTemplate.Models)
                    {
                        var command = new SQLiteCommand("INSERT INTO template (id, title, text, date) VALUES (@Id, @Name, @Content, @Date)", connection);
                        command.Parameters.AddWithValue("@Id", template.Id);
                        command.Parameters.AddWithValue("@Name", template.Title);
                        command.Parameters.AddWithValue("@Content", template.Content);
                        command.Parameters.AddWithValue("@Date", template.Date);

                        await command.ExecuteNonQueryAsync();
                    }
                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    VMLocator.ProgressViewModel.Hide();
                    throw new Exception($"Failed to download templates: {ex.Message} {ex.StackTrace}");
                }
            }

            var resultChatLog = await supabase.From<ChatRoom>().Get();

            VMLocator.ProgressViewModel.ProgressValue = 0.75;

            using (var transaction = connection.BeginTransaction())
            {

                try
                {
                    foreach (var chatLog in resultChatLog.Models)
                    {
                        var resultMessage = await supabase.From<Message>().Where(x => x.RoomId == chatLog.Id).Order(x => x.Id, Ordering.Ascending).Get();

                        string combinedMessage = CombineMessage(resultMessage.Models);

                        var command = new SQLiteCommand("INSERT INTO chatlog (id, date, title, json, text, category, lastprompt, jsonprev) VALUES (@Id, @UpdatedOn, @Title, @Json, @Message, @Category, @LastPrompt, @JsonPrev)", connection);
                        command.Parameters.AddWithValue("@Id", chatLog.Id);
                        command.Parameters.AddWithValue("@UpdatedOn", chatLog.UpdatedOn);
                        command.Parameters.AddWithValue("@Title", chatLog.Title);
                        command.Parameters.AddWithValue("@Json", chatLog.Json);
                        command.Parameters.AddWithValue("@Message", combinedMessage);
                        command.Parameters.AddWithValue("@Category", chatLog.Category);
                        command.Parameters.AddWithValue("@LastPrompt", chatLog.LastPrompt);
                        command.Parameters.AddWithValue("@JsonPrev", chatLog.JsonPrev);

                        await command.ExecuteNonQueryAsync();
                    }
                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    VMLocator.ProgressViewModel.Hide();
                    throw new Exception($"Failed to download chat logs: {ex.Message} {ex.StackTrace}");
                }
            }


            try
            {
                VMLocator.ProgressViewModel.ProgressText = "Display is being updated...";
                VMLocator.ProgressViewModel.ProgressValue = 1;

                // インメモリをいったん閉じてまた開く
                await DatabaseProcess.memoryConnection!.CloseAsync();
                await _dbProcess.DbLoadToMemoryAsync();
                VMLocator.DataGridViewModel.DataGridIsFocused = false;
                VMLocator.DataGridViewModel.ChatList = await _dbProcess.SearchChatDatabaseAsync();
                VMLocator.DataGridViewModel.SelectedItemIndex = -1;
                await _dbProcess.GetEditorLogDatabaseAsync();
                await _dbProcess.GetTemplateItemsAsync();
                await VMLocator.MainViewModel.LoadPhraseItemsAsync();
                VMLocator.EditorViewModel.SelectedEditorLogIndex = -1;
                VMLocator.EditorViewModel.SelectedTemplateItemIndex = -1;
                VMLocator.ProgressViewModel.Hide();
            }
            catch (Exception ex)
            {
                VMLocator.ProgressViewModel.Hide();
                throw new Exception($"Error during display update: {ex.Message} {ex.StackTrace}");
            }
        }

        // -------------------------------------------------------------------------------------------------------
        public string CombineMessage(List<Message> messages)
        {
            string combinedMessage = "";
            string br = Environment.NewLine;
            foreach (var message in messages)
            {
                string date = message.CreatedOn.ToString();
                if (message.CreatedOn.Year < 0002)
                {
                    date = "Web Chat";
                }
                string role = message.Role == "assistant" ? "AI" : "You";
                combinedMessage = $"{combinedMessage}[{date}] by {role}{br}{br}";
                combinedMessage = $"{combinedMessage}{message.Content}{br}{br}{message.Usage}";
                combinedMessage = combinedMessage.Trim() + br + br;
            }
            return combinedMessage;
        }

        // -------------------------------------------------------------------------------------------------------
        public List<Message> DivideMessage(string message, int roomId, string uid)
        {
            var models = new List<Message>();

            string normarizedContent = Regex.Replace(message, @"\r\n|\r|\n", Environment.NewLine);

            var chatLogRegex = new Regex(@"^\[(.+)\] by (You|AI)", RegexOptions.Multiline);
            var usageRegex = new Regex(@"^usage=.+$", RegexOptions.Multiline);
            var systemOffRegex = new Regex(@$"#(\s*)(?i)system({Environment.NewLine})*?---({Environment.NewLine})*", RegexOptions.Singleline);
            var systemMessageRegex = new Regex(@$"#(\s*)(?i)system({Environment.NewLine})*(.+?)---({Environment.NewLine})*", RegexOptions.Singleline);

            MatchCollection matches = chatLogRegex.Matches(normarizedContent);

            for (int i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                var isUser = match.Groups[2].Value == "You";
                var role = isUser ? "user" : "assistant";
                DateTime timestamp;
                if (DateTime.TryParse(match.Groups[1].Value, out DateTime result))
                {
                    timestamp = result;
                }
                else
                {
                    timestamp = DateTime.MinValue;
                }

                var endOfMatch = match.Index + match.Length;
                var nextMatchIndex = i < matches.Count - 1 ? matches[i + 1].Index : normarizedContent.Length;
                var content = normarizedContent.Substring(endOfMatch, nextMatchIndex - endOfMatch);

                var systemOffMatch = systemOffRegex.Match(content);
                if (systemOffMatch.Success)
                {
                    models.Add(new Message { UserId = uid, RoomId = roomId, CreatedOn = timestamp, Content = "System messages were turned off.", Role = "system" });
                    content = content.Replace(systemOffMatch.Value, "").Trim('\r', '\n');
                }

                var systemMessageMatch = systemMessageRegex.Match(content);
                if (systemMessageMatch.Success)
                {
                    models.Add(new Message { UserId = uid, RoomId = roomId, CreatedOn = timestamp, Content = systemMessageMatch.Value, Role = "system" });
                    content = content.Replace(systemMessageMatch.Value, "").Trim('\r', '\n');
                }

                var usageMatch = usageRegex.Match(content);
                if (usageMatch.Success)
                {
                    content = content.Substring(0, usageMatch.Index).Trim('\r', '\n');
                    var usageStr = Regex.Replace(usageMatch.Value, "usage={\"prompt_tokens\":([0-9]+),\"completion_tokens\":([0-9]+),\"total_tokens\":([0-9]+)}", "[tokens] prompt:$1, completion:$2, total:$3");
                    if (content.Length > 0)
                    {
                        models.Add(new Message { UserId = uid, RoomId = roomId, CreatedOn = timestamp, Content = content, Role = role, Usage = usageStr });
                    }
                }
                else
                {
                    content = content.Trim('\r', '\n');
                    if (content.Length > 0)
                    {
                        models.Add(new Message { UserId = uid, RoomId = roomId, CreatedOn = timestamp, Content = content, Role = role });
                    }
                }
            }

            return models;
        }
    }
}
