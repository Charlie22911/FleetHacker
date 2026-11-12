using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.IO.Compression;
namespace MediaReviewer {
    static class FilePropertiesSmoke {
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        static void Wait(Func<bool> condition){var clock=Stopwatch.StartNew();while(!condition()){if(clock.Elapsed.TotalSeconds>40)throw new Exception("File properties check timed out.");Thread.Sleep(10);}}
        static UiResult Result(ReviewerEngine engine,string id){UiResult result=null;Wait(()=>{UiResult next;while(engine.TryTakeResult(out next)){if(next.RequestId==id){result=next;return true;}if(next.Evidence!=null)next.Evidence.Dispose();}return false;});return result;}
        static UiResult Success(ReviewerEngine engine,string id){var result=Result(engine,id);Check(result.Success,result.Error);return result;}
        static void WaitUi(Func<bool> condition){var clock=Stopwatch.StartNew();while(!condition()){if(clock.Elapsed.TotalSeconds>30)throw new Exception("File properties UI timed out.");var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(10);}}
        static System.Collections.Generic.IEnumerable<T> Controls<T>(DependencyObject node) where T:DependencyObject {var found=node as T;if(found!=null)yield return found;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Controls<T>(VisualTreeHelper.GetChild(node,i)))yield return child;}
        static void Ui(string directory,string path){
            var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
            using(var controller=new ReviewerWindow(Program.ReadWindow(),false)){
                controller.Window.Show();controller.Open(path,false);
                var engine=(ReviewerEngine)typeof(ReviewerWindow).GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);WaitUi(()=>engine.Ready);
                MediaRow row;using(var db=new SqliteConnection(path,true))row=Catalog.Page(db,new PageRequest {Bucket="NON_COMPLIANT"}).Rows.Single();
                typeof(ReviewerWindow).GetMethod("Details",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,new object[]{row});
                var details=controller.Window.OwnedWindows.OfType<Window>().Single(w=>w.Title=="Item details and notes");
                Expander section=null;WaitUi(()=>{details.UpdateLayout();section=Controls<Expander>(details).FirstOrDefault(e=>e.Name=="SavedFilePropertiesSection");return section!=null;});var text=((StackPanel)section.Content).Children.OfType<TextBox>().Single();
                WaitUi(()=>text.Text.Contains("Owner SID:")&&text.Text.Contains("Date created:"));Check(!section.IsExpanded,"File properties must start collapsed.");
                var noteSection=Controls<Expander>(details).Single(e=>Convert.ToString(e.Header).StartsWith("Notes"));noteSection.IsExpanded=true;details.UpdateLayout();var notes=((StackPanel)noteSection.Content).Children.OfType<TextBox>().Single();double height=notes.ActualHeight;section.IsExpanded=true;details.UpdateLayout();
                Check(text.ActualHeight<=150.1 && notes.ActualHeight==height,"Expanding properties must not stretch notes or the window.");
                var bitmap=new RenderTargetBitmap((int)details.ActualWidth,(int)details.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(details);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(directory,"file-properties.png")))encoder.Save(output);
                details.Close();controller.Window.Close();
            }app.Shutdown();
        }
        static void Migration(string directory){
            string path=Path.Combine(directory,"older-encrypted.sqlite");const string password="generated migration database password";
            using(var key=new SessionPassword(Encoding.UTF8.GetBytes(password)))using(var db=new SqliteConnection(path,false,key)){
                db.Script(Catalog.Schema+Migrations.Schema2+Migrations.Schema3+Migrations.Schema4+Migrations.Schema5+Migrations.Schema6+Migrations.Schema7+Migrations.Schema8+Migrations.Schema9+Migrations.Schema10+Migrations.Schema11+Migrations.Schema12+Migrations.Schema13+Migrations.Schema14+Migrations.Schema15+Migrations.Schema16);
                db.Script("PRAGMA application_id="+Catalog.ApplicationId+"; PRAGMA user_version=16;");db.Execute("INSERT INTO app_info VALUES('workspace_id','generated-migration-workspace'),('revision','0')");
                string root=Catalog.Root(db,directory);Catalog.Publish(db,new FileRecord {RootId=root,RelativePath="legacy.png",Name="legacy.png",Extension="png",Size=4,ModifiedTicks=new DateTime(2020,1,1,0,0,0,DateTimeKind.Utc).Ticks,FileIdentity="generated-legacy",Hash=new string('a',64)});
                using(var supervisor=new SessionPassword(Encoding.UTF8.GetBytes("different migration supervisor password")))DatabaseProtection.CreateSupervisor(db,supervisor);
            }
            using(var engine=ReviewerEngine.OpenProtected(path,false,new DatabaseCredentials(Encoding.UTF8.GetBytes(password)))){
                Wait(()=>engine.Ready||engine.Faulted);Check(engine.Ready,engine.Status.Error);
                var page=Success(engine,engine.RequestPage(new PageRequest())).Page;var locations=Success(engine,engine.RequestLocations(page.Rows.Single().GroupId,"",100)).Locations;
                Check(locations.Rows.Single().Properties==null && locations.Rows.Single().FilePropertiesText.Contains("not been recorded"),"Migration must not invent ownership or creation dates for older rows.");
                using(var db=new SqliteConnection(path,true))Check(Convert.ToInt64(db.Scalar("PRAGMA user_version"))==17,"Existing encrypted databases must migrate to schema 17.");
            }
            string backup=Directory.GetFiles(directory,"older-encrypted.sqlite.pre-upgrade-v16-*.sqlite").Single();Check(DatabaseProtection.IsEncrypted(backup),"Automatic metadata migration backup must remain encrypted.");
            using(var key=new SessionPassword(Encoding.UTF8.GetBytes(password)))using(var db=new SqliteConnection(backup,true,key))Check(Convert.ToInt64(db.Scalar("PRAGMA user_version"))==16,"Pre-upgrade backup must preserve the old schema and password.");
        }
        static void Archive(string directory){
            string root=Directory.CreateDirectory(Path.Combine(directory,"archive-source")).FullName;string zip=Path.Combine(root,"images.zip");
            using(var archive=new ZipArchive(File.Create(zip),ZipArchiveMode.Create))using(var output=archive.CreateEntry("sample.png").Open()){byte[] bytes=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");output.Write(bytes,0,bytes.Length);}
            string path=Path.Combine(directory,"archive.sqlite");using(var engine=ReviewerEngine.Open(path,true)){
                Wait(()=>engine.Ready||engine.Faulted);Check(engine.Ready,engine.Status.Error);string scan=engine.StartScan(new ScanOptions {RootPath=root,WorkerCount=1,Extensions=new[]{"png"},ScanArchives=true});Wait(()=>engine.Status.ScanId==scan&&!engine.Status.RunningScan);
                using(var db=new SqliteConnection(path,true)){var row=db.Query("SELECT created_ticks,owner_sid,owner_state,origin FROM file_properties",null,10).Single();Check(row[0]==null && (string)row[1]=="" && (string)row[2]=="not_applicable" && (string)row[3]=="archive_member","Archive entries must not inherit container ownership or a fabricated creation date.");}
                var item=Success(engine,engine.RequestPage(new PageRequest())).Page.Rows.Single();Success(engine,engine.RequestVaultUnlock(Encoding.UTF8.GetBytes("generated archive supervisor"),true));
                Success(engine,engine.RequestReview(new ReviewRequest {GroupIds=new[]{item.GroupId},Status="NON_COMPLIANT",OperationId=Guid.NewGuid().ToString("N")}));
                using(var original=Success(engine,engine.RequestEvidenceInspect(item.GroupId)).Evidence)Check(original.Verified&&!original.Historical,"An available archive entry must retain its actual original bytes.");
            }
        }
        static void OtherCard(string directory){
            string path=Path.Combine(directory,"other-card.sqlite");
            string first=Directory.CreateDirectory(Path.Combine(directory,"first-root")).FullName,second=Directory.CreateDirectory(Path.Combine(directory,"second-root")).FullName;
            byte[] bytes=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
            File.WriteAllBytes(Path.Combine(first,"first.png"),bytes);File.WriteAllBytes(Path.Combine(second,"second.png"),bytes);
            using(var engine=ReviewerEngine.Open(path,true)){
                Wait(()=>engine.Ready||engine.Faulted);Check(engine.Ready,engine.Status.Error);
                foreach(string root in new[]{first,second}){string scan=engine.StartScan(new ScanOptions {RootPath=root,WorkerCount=1,Extensions=new[]{"png"}});Wait(()=>engine.Status.ScanId==scan&&!engine.Status.RunningScan);}
                var cards=Success(engine,engine.RequestPage(new PageRequest())).Page.Rows;Check(cards.Count==2,"Separate roots must retain separate review cards.");
                var locations=Success(engine,engine.RequestLocations(cards[0].GroupId,"",100)).Locations;var other=locations.Rows.Single(r=>r.OtherCard);
                Check(other.GroupId==cards[1].GroupId,"A matching location must identify its own card for refresh.");Success(engine,engine.RequestFilePropertiesRefresh(other.GroupId,other.Id));
            }
            using(var db=new SqliteConnection(path,false)){
                string id=(string)db.Scalar("SELECT occurrence_id FROM file_properties LIMIT 1");db.Execute("UPDATE file_properties SET origin=? WHERE occurrence_id=?",new string('x',1024*1024),id);
                var value=FilePropertiesData.Read(db,id);Check(value.Origin=="unknown" && value.OwnerState=="unavailable" && value.Cost<200000,"Unsupported metadata types must remain bounded and unavailable.");
            }
        }
        static void CapturedPropertiesDisplay(){
            foreach(bool notice in new[]{false,true}){
                var metadata=new System.Collections.Generic.Dictionary<string,object>{{"notes",new string('n',16384)},{"fileProperties",new SavedFileProperties {OwnerSid="S-1-5-18",OwnerName="Generated owner",CreatedTicks=new DateTime(2014,1,1).Ticks,ModifiedTicks=new DateTime(2020,1,1).Ticks}}};
                if(notice)metadata.Add("displayNotice","Extended review text remains preserved.");
                using(var original=new EvidenceInspection {Metadata=Encoding.UTF8.GetBytes(EvidenceFormat.Json().Serialize(metadata))}){string text=EvidencePackageSession.Describe(original);Check(text.Contains("Windows owner: Generated owner")&&text.Contains("Date created: 2014-01-01"),"Capture properties must remain visible with long notes or a bounded-text notice.");}
            }
        }
        static void OfflineReview(string directory){
            string root=Directory.CreateDirectory(Path.Combine(directory,"offline-source")).FullName,path=Path.Combine(directory,"offline-review.sqlite"),image=Path.Combine(root,"missing.png"),moved=Path.Combine(directory,"moved-original.png");
            File.WriteAllBytes(image,Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
            using(var engine=ReviewerEngine.Open(path,true)){
                Wait(()=>engine.Ready||engine.Faulted);Check(engine.Ready,engine.Status.Error);string scan=engine.StartScan(new ScanOptions {RootPath=root,WorkerCount=1,Extensions=new[]{"png"}});Wait(()=>engine.Status.ScanId==scan&&!engine.Status.RunningScan);
                var item=Success(engine,engine.RequestPage(new PageRequest())).Page.Rows.Single();File.Move(image,moved);
                Success(engine,engine.RequestVaultUnlock(Encoding.UTF8.GetBytes("generated offline original password"),true));
                // A missing source needs no retention space, even when a cap is below its recorded size.
                Success(engine,engine.RequestOriginalLimits(.000000001,.000000001));
                var review=new ReviewRequest {Status="NON_COMPLIANT",GroupIds=new[]{item.GroupId},OperationId=Guid.NewGuid().ToString("N"),Notes="Saved historical review",ExpectedRevisions=new System.Collections.Generic.Dictionary<long,long>{{item.GroupId,item.ReviewRevision}}};
                var saved=Success(engine,engine.RequestReview(review));Check(saved.Warning.Contains("unavailable")&&saved.ReviewRevisions.ContainsKey(item.GroupId),"Offline classification must warn about the original and remain undoable.");
                var replay=Success(engine,engine.RequestReview(review));Check(replay.Reconciled&&replay.Warning.Contains("unavailable"),"Replaying an offline review must preserve its unavailable-original outcome.");
                var evidenceRequest=new ReviewRequest {Status="EVIDENCE",GroupIds=new[]{item.GroupId},OperationId=Guid.NewGuid().ToString("N"),Notes="Generated historical Evidence note"};
                var evidence=Success(engine,engine.RequestOriginalRetention(evidenceRequest));Check(evidence.Warning.Contains("historical Evidence"),"Evidence completion must identify a historical capture.");
                using(var original=Success(engine,engine.RequestEvidenceInspect(item.GroupId)).Evidence)Check(original.Historical&&!original.Verified&&original.Preview!=null&&EvidencePackageSession.Describe(original).Contains("Owner SID:")&&EvidencePackageSession.Describe(original).Contains(review.Notes),"Historical Evidence must authenticate its saved preview, ownership and prior notes without claiming original-byte verification.");
                string exported=Path.Combine(directory,"unavailable-original.png");Check(!Result(engine,engine.RequestEvidenceOriginalExport(item.GroupId,exported,Guid.NewGuid().ToString("N"))).Success&&!File.Exists(exported),"Historical Evidence must not export a fabricated original.");
                string package=Path.Combine(directory,"historical-evidence.mcrevidence");Success(engine,engine.RequestEvidencePackageExport(new[]{item.GroupId},package,Guid.NewGuid().ToString("N")));
                using(var reader=EvidencePackageReader.Open(package,CancellationToken.None,null)){reader.Unlock(Encoding.UTF8.GetBytes("generated offline original password"),CancellationToken.None,null);Check(reader.Items.Single().Historical,"Protected packages must identify historical records.");using(var original=reader.Inspect(0,CancellationToken.None,null))Check(original.Historical&&original.Preview!=null&&EvidencePackageSession.Describe(original).Contains(review.Notes),"Protected packages must retain authenticated historical details and previews.");bool blocked=false;try{reader.ExportOriginal(0,exported,CancellationToken.None,null);}catch(InvalidOperationException){blocked=true;}Check(blocked&&!File.Exists(exported),"Package readers must block original export for historical records.");}
                string report=Path.Combine(directory,"historical-evidence.csv");Success(engine,engine.RequestReport(new ReportOptions {Destination=report,IncludeEvidence=true}));Check(File.ReadAllText(report).Contains("Historical Evidence (original unavailable)"),"Reports must distinguish historical Evidence from retained originals.");
                Success(engine,engine.RequestOriginalRetention(evidenceRequest));
                Success(engine,engine.RequestVaultLock());Check(!Result(engine,engine.RequestEvidenceInspect(item.GroupId)).Success,"Supervisor locking must protect historical Evidence.");Success(engine,engine.RequestVaultUnlock(Encoding.UTF8.GetBytes("generated offline original password"),false));
                using(var db=new SqliteConnection(path,false)){byte[] cipher=(byte[])db.Scalar("SELECT metadata_ciphertext FROM historical_evidence WHERE state='complete' LIMIT 1");var altered=(byte[])cipher.Clone();altered[0]^=1;db.Execute("UPDATE historical_evidence SET metadata_ciphertext=? WHERE state='complete'",altered);Check(!Result(engine,engine.RequestEvidenceInspect(item.GroupId)).Success,"Historical metadata tampering must fail authentication.");db.Execute("UPDATE historical_evidence SET metadata_ciphertext=? WHERE state='complete'",cipher);}
                using(var db=new SqliteConnection(path,false)){
                    string digest=(string)db.Scalar("SELECT digest FROM contents WHERE id=?",item.ContentId);db.Execute("UPDATE contents SET digest=? WHERE id=?",new string('b',64),item.ContentId);Check(!Result(engine,engine.RequestEvidenceInspect(item.GroupId)).Success,"Historical Evidence must remain bound to the selected recorded fingerprint.");db.Execute("UPDATE contents SET digest=? WHERE id=?",digest,item.ContentId);
                    byte[] iv=(byte[])db.Scalar("SELECT preview_iv FROM historical_evidence WHERE state='complete' LIMIT 1"),cipher=(byte[])db.Scalar("SELECT preview_ciphertext FROM historical_evidence WHERE state='complete' LIMIT 1");db.Execute("UPDATE historical_evidence SET preview_iv=NULL,preview_ciphertext=NULL WHERE state='complete'");Check(!Result(engine,engine.RequestEvidenceInspect(item.GroupId)).Success,"Removing a saved historical preview must fail authentication.");db.Execute("UPDATE historical_evidence SET preview_iv=?,preview_ciphertext=? WHERE state='complete'",iv,cipher);
                }
                Success(engine,engine.RequestEvidenceReclassify(new ReviewRequest {Status="NON_COMPLIANT",GroupIds=new[]{item.GroupId},OperationId=Guid.NewGuid().ToString("N"),Notes=review.Notes}));
                using(var db=new SqliteConnection(path,true))Check(Convert.ToInt64(db.Scalar("SELECT COUNT(*) FROM original_links"))==0,"Historical Evidence must not claim a retained original.");
                Success(engine,engine.RequestOriginalLimits(0,0));File.Move(moved,image);
                var current=Success(engine,engine.RequestPage(new PageRequest {Bucket="NON_COMPLIANT"})).Page.Rows.Single();
                Success(engine,engine.RequestRetainOriginals(new[]{item.GroupId},new System.Collections.Generic.Dictionary<long,long>{{item.GroupId,current.ReviewRevision}},Guid.NewGuid().ToString("N")));
                using(var db=new SqliteConnection(path,true))Check(Convert.ToInt64(db.Scalar("SELECT COUNT(*) FROM original_links"))==1&&(string)db.Scalar("SELECT notes FROM review_groups WHERE id=?",item.GroupId)==review.Notes,"Retaining a reconnected original must preserve its historical review.");
            }
        }
        static void MetadataOnlyEvidence(string directory){
            string path=Path.Combine(directory,"metadata-only.sqlite");
            using(var engine=ReviewerEngine.Open(path,true)){
                Wait(()=>engine.Ready||engine.Faulted);Check(engine.Ready,engine.Status.Error);
                long group=(long)engine.WaitWrite(db=>{string root=Catalog.Root(db,directory);long id=Catalog.Publish(db,new FileRecord {RootId=root,RelativePath="deleted-before-preview.png",Name="deleted-before-preview.png",Extension="png",Size=123,ModifiedTicks=new DateTime(2010,1,1).Ticks,FileIdentity="generated-missing-original",Hash=new string('a',64)});db.Execute("DELETE FROM occurrences");return id;});
                Success(engine,engine.RequestVaultUnlock(Encoding.UTF8.GetBytes("generated metadata-only supervisor"),true));
                Success(engine,engine.RequestOriginalRetention(new ReviewRequest {GroupIds=new[]{group},Status="EVIDENCE",OperationId=Guid.NewGuid().ToString("N"),Notes="Historical record without a preview"}));
                using(var saved=Success(engine,engine.RequestEvidenceInspect(group)).Evidence)Check(saved.Historical&&!saved.Verified&&saved.Preview==null&&EvidencePackageSession.Describe(saved).Contains("Historical record without a preview"),"Evidence must retain metadata even without any original or preview.");
                var page=Success(engine,engine.RequestPage(new PageRequest {Bucket="EVIDENCE"})).Page;Check(page.Rows.Single().HistoricalEvidence&&!page.Rows.Single().OriginalRetained,"Metadata-only Evidence must remain visible without claiming retained original bytes.");
                engine.WaitWrite(db=>{string operation=Guid.NewGuid().ToString("N");db.Execute("INSERT INTO original_requests VALUES(?,?,'staging',?)",operation,new string('c',64),Catalog.Now());db.Execute("INSERT INTO historical_evidence SELECT ?,?,group_id,content_id,decision_key,hash,size,'staging',created_at,metadata_iv,metadata_ciphertext,preview_iv,preview_ciphertext FROM historical_evidence WHERE state='complete' LIMIT 1",Guid.NewGuid().ToString("N"),operation);return null;});
                Success(engine,engine.RequestOriginalCleanup());using(var db=new SqliteConnection(path,true))Check(Convert.ToInt64(db.Scalar("SELECT COUNT(*) FROM historical_evidence WHERE state<>'complete'"))==0&&Convert.ToInt64(db.Scalar("SELECT COUNT(*) FROM historical_evidence WHERE state='complete'"))==1,"Cleanup must remove unfinished historical-only captures and preserve completed Evidence.");
                Success(engine,engine.RequestReport(new ReportOptions {Destination=Path.Combine(directory,"metadata-only.csv"),IncludeEvidence=true}));
                string package=Path.Combine(directory,"metadata-only.mcrevidence");Success(engine,engine.RequestEvidencePackageExport(new[]{group},package,Guid.NewGuid().ToString("N")));
                using(var reader=EvidencePackageReader.Open(package,CancellationToken.None,null)){reader.Unlock(Encoding.UTF8.GetBytes("generated metadata-only supervisor"),CancellationToken.None,null);using(var saved=reader.Inspect(0,CancellationToken.None,null))Check(saved.Historical&&saved.Preview==null,"Metadata-only records must survive protected package export.");}
                Success(engine,engine.RequestEvidenceReclassify(new ReviewRequest {GroupIds=new[]{group},Status="NON_COMPLIANT",OperationId=Guid.NewGuid().ToString("N")}));Check(Success(engine,engine.RequestPage(new PageRequest {Bucket="NON_COMPLIANT"})).Page.Rows.Single().HistoricalEvidence,"Historical records without locations must remain visible after reclassification.");
            }
        }
        internal static int Run(string directory){
            Directory.CreateDirectory(directory);string root=Directory.CreateDirectory(Path.Combine(directory,"photos")).FullName;
            string image=Path.Combine(root,"first.png");File.WriteAllBytes(image,Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
            File.SetCreationTimeUtc(image,new DateTime(2012,3,4,5,6,7,DateTimeKind.Utc));
            string duplicate=Path.Combine(root,"copy.png");File.Copy(image,duplicate);File.SetCreationTimeUtc(duplicate,new DateTime(2017,6,7,8,9,10,DateTimeKind.Utc));
            string path=Path.Combine(directory,"properties.sqlite");
            using(var engine=ReviewerEngine.Open(path,true)){
                Wait(()=>engine.Ready||engine.Faulted);Check(engine.Ready,engine.Status.Error);
                string scan=engine.StartScan(new ScanOptions {RootPath=root,WorkerCount=1,Extensions=new[]{"png"}});Wait(()=>engine.Status.ScanId==scan&&!engine.Status.RunningScan);
                Check(!engine.Faulted&&String.IsNullOrEmpty(engine.Status.Error),engine.Status.Error);
                using(var db=new SqliteConnection(path,true)){
                    Check(db.Scalar("SELECT 1 FROM sqlite_schema WHERE name='file_properties'")!=null,"File properties must be saved during scanning, rather than requiring a live source later.");
                    var rows=db.Query("SELECT f.created_ticks,f.owner_sid,f.owner_name,f.observed_at FROM file_properties f JOIN occurrences o ON o.id=f.occurrence_id ORDER BY o.name",null,10);
                    Check(rows.Count==2 && (long)rows[0][0]!=(long)rows[1][0],"Duplicate locations must retain different creation dates.");
                    Check(rows.All(r=>!String.IsNullOrEmpty(r[1] as string)&&!String.IsNullOrEmpty(r[3] as string)),"Windows owner SID and observation time must be recorded.");
                    string expected=File.GetAccessControl(image,AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier)).Value;Check(rows.All(r=>(string)r[1]==expected),"Recorded ownership must match Windows security information, not the reviewer label.");
                    Check(db.Query("PRAGMA table_info(file_properties)",null,30).All(r=>!((string)r[1]).Contains("creator")&&!((string)r[1]).Contains("author")),"Creator/author fields are outside this feature.");
                }
                var page=Success(engine,engine.RequestPage(new PageRequest())).Page;var item=page.Rows.Single();
                var locations=Success(engine,engine.RequestLocations(item.GroupId,"",100)).Locations;
                var first=locations.Rows.Single(r=>r.Path=="first.png");var copy=locations.Rows.Single(r=>r.Path=="copy.png");
                Check(first.Properties.CreatedTicks!=copy.Properties.CreatedTicks,"Details must preserve per-location dates on one duplicate card.");
                File.SetCreationTimeUtc(image,new DateTime(2019,1,2,3,4,5,DateTimeKind.Utc));Success(engine,engine.RequestFilePropertiesRefresh(item.GroupId,first.Id));
                using(var db=new SqliteConnection(path,true))Check(FilePropertiesData.Read(db,first.Id).CreatedTicks==File.GetCreationTimeUtc(image).Ticks,"Refreshing an existing item must save its updated creation time.");
                DateTime modified=File.GetLastWriteTimeUtc(image);File.SetLastWriteTimeUtc(image,modified.AddHours(1));
                Check(!Result(engine,engine.RequestFilePropertiesRefresh(item.GroupId,first.Id)).Success,"Changed sources must be rejected instead of relabeling saved metadata.");File.SetLastWriteTimeUtc(image,modified);
                Success(engine,engine.RequestVaultUnlock(Encoding.UTF8.GetBytes("generated ownership vault password"),true));
                Success(engine,engine.RequestReview(new ReviewRequest {GroupIds=new[]{item.GroupId},Status="NON_COMPLIANT",OperationId=Guid.NewGuid().ToString("N"),Reviewer="Generated reviewer",Notes="Generated ownership fixture"}));
                string report=Path.Combine(directory,"properties.csv");Success(engine,engine.RequestReport(new ReportOptions {Destination=report}));var csv=File.ReadAllText(report);
                Check(csv.Contains("File created UTC")&&csv.Contains("Owner SID")&&csv.Contains("2019-01-02")&&csv.Contains("2017-06-07"),"Reports must export saved dates for both locations.");
                Success(engine,engine.RequestBackup(Path.Combine(directory,"ui-properties.sqlite")));
                File.Delete(image);File.Delete(duplicate);
                var offline=Success(engine,engine.RequestLocations(item.GroupId,"",100)).Locations;Check(offline.Rows.All(r=>r.FilePropertiesText.Contains("Owner SID:")),"Offline Details must show saved ownership.");
                Check(!Result(engine,engine.RequestFilePropertiesRefresh(item.GroupId,first.Id)).Success,"Offline refresh must fail without erasing saved metadata.");
                var sheet=Success(engine,engine.RequestCaseSheet(item)).Value;Check(sheet.Contains("Saved file properties")&&sheet.Contains("Owner SID:"),"Offline case sheets must retain ownership and dates.");
                Success(engine,engine.RequestOriginalRetention(new ReviewRequest {GroupIds=new[]{item.GroupId},Status="EVIDENCE",OperationId=Guid.NewGuid().ToString("N")}));
                var inspected=Success(engine,engine.RequestEvidenceInspect(item.GroupId));using(var original=inspected.Evidence)Check(EvidencePackageSession.Describe(original).Contains("File properties at capture")&&EvidencePackageSession.Describe(original).Contains("Owner SID:"),"Authenticated Evidence details must retain captured ownership and dates.");
                string evidenceReport=Path.Combine(directory,"evidence-properties.csv");Success(engine,engine.RequestReport(new ReportOptions {Destination=evidenceReport,IncludeEvidence=true}));Check(File.ReadAllText(evidenceReport).Contains("Owner SID"),"Evidence reports must include authenticated file properties.");
                Check(!Result(engine,engine.RequestFilePropertiesRefresh(item.GroupId,first.Id)).Success,"Ordinary metadata refresh must not bypass Evidence protection.");
            }
            using(var db=new SqliteConnection(path,true))Check(Convert.ToInt64(db.Scalar("SELECT COUNT(*) FROM file_properties"))==2,"Saved properties must remain available offline.");
            Migration(directory);Archive(directory);OtherCard(directory);CapturedPropertiesDisplay();OfflineReview(directory);MetadataOnlyEvidence(directory);
            Ui(directory,Path.Combine(directory,"ui-properties.sqlite"));
            Console.WriteLine("Saved ownership, duplicate creation dates and offline persistence passed.");return 0;
        }
    }
}
