using System;
using System.Linq;
using System.Collections.Generic;
using quizartsocial_backend.Models;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Net;
using System.Text;
using NotificationEngine.Services;
using quizartsocial_backend.Services;

namespace quizartsocial_backend
{
    public class TopicRepo : ITopic
    {
        SocialContext context;
        GraphDb graphobj;
        public TopicRepo(SocialContext _context, GraphDb _graph)
        {
            this.context = _context;
            this.graphobj = _graph;
        }
        public async Task<List<Post>> GetPostsForTopicAsync(string topicName)
        {
            List<Post> posts = await context.Topics
                               .Where(t => t.topicName == topicName)
                               .Include("posts").SelectMany(s => s.posts)
                               .Include("comments")
                               .ToListAsync();
            return posts;
        }

        // A user follows certain topics
        // Hence it becomes logical to show those posts which
        // were created recently for the followed topics.

        // First for a given user 
        // get the ids of the post which are relevant
        // to him based on above condition from Neo4j.
        // Then get the exact posts from SQL based on Ids
        // fetched from Neo4j.

        public async Task<List<Post>> GetPersonalisedPostsAsync(string u_id)
        {
            var topics = await graphobj.graph.Cypher
                        .OptionalMatch("(u:User)-[:follows]-(t:Topic)")
                        .Where((User u) => u.userId == u_id)
                        .Return((u, t) => new
                        {
                            User = u.As<User>(),
                            Topic = t.CollectAs<Topic>()
                        })
                        .ResultsAsync;
            var topicslist = topics.SelectMany(c=>c.Topic).ToList();
            
            List<Post> listOfPosts = new List<Post>();
            foreach(var topic in topicslist)
            {
                Topic test = new Topic();
                test = topic;
                var postsOfATopic = context.Topics
                .Where(t => t.topicId == topic.topicId)
                .Include("posts").SelectMany(s => s.posts)
                .Include("comments")
                .ToList();
                listOfPosts.AddRange(postsOfATopic);
            }

            return listOfPosts;

        }

        public async Task<Post> GetPostByIdAsyncFromDB(int postId)
        {
            Post post = await context.Posts
                        .FirstOrDefaultAsync(t => t.postId == postId);
            return post;
        }

        public async Task<List<Topic>> FetchTopicsFromDbAsync()
        {
            // Followed Topics.
            // Topics on which games are played.
            // Then, other topics.

            // Topic test1 = new Topic();
            // Topic test2 = new Topic();
            // test1.topicName = "book";
            // test1.topicImage = "sad";
            // await AddTopicToDBAsync(test1);
            // await AddTopicToDBAsync(test2);
            List<Topic> res = await context.Topics.ToListAsync();
            return res;
        }

        public async Task AddTopicToDBAsync(Topic obj)
        {
            try 
            {
                Console.WriteLine("---------{0}----------", obj.topicName);
                if (context.Topics.FirstOrDefault(n => n.topicName == obj.topicName) == null)
                {
                    Console.WriteLine("rabbit -topic getting inserted---", obj.topicName);
                    await context.Topics.AddAsync(obj);
                    await context.SaveChangesAsync();
                }

                await this.graphobj.graph.Cypher
                    .Create("(t:Topic)")
                    .Set("t={obj}")
                    .WithParams(new
                    {
                        obj
                    })
                    .ExecuteWithoutResultsAsync();

            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            }
            // Needs logic to create topics in GraphDB + SQL.            
        }

        public async Task DelTopicFromDBAsync(string topicName)
        {
            Console.WriteLine("-----------------entered--------------");
            var topic = await context.Topics.FirstOrDefaultAsync(s => s.topicName == topicName);
            if (topic != null)
            {
                Console.WriteLine("-----------------name----------------" + topic.topicName);
                context.Topics.Remove(topic);
                Console.WriteLine("-----------removed------------");
            }
            await context.SaveChangesAsync();
        }

        public async Task DelTopicByIdAsync(int id)
        {
            var topic = await context.Topics.FirstOrDefaultAsync(s => s.topicId == id);
            if (topic != null)
            {
                Console.WriteLine("-----------------name----------------" + topic.topicName);
                context.Topics.Remove(topic);
                Console.WriteLine("-----------removed------------");
            }
            await context.SaveChangesAsync();
        }


        public async Task AddPostToDBAsync(Post post)
        {
            // Needs to store the complete post in SQL
            // And only the Id of the post in Neo4j.
            // Also needs to create relationships between
            // the post-author, post-topic.

            var query = graphobj.graph.Cypher
                        .Match("(u:User)", "(t:Topic)")
                        .Where((User u) => u.userId == post.userId)
                        .AndWhere((Topic t) => t.topicId == post.topicId)
                        .Create("(u)-[:created]->(p:Post {post})-[:onTopic]->(t)")
                        .WithParams(new
                        {
                            post
                        });

            Console.WriteLine(query.Query.QueryText);
            await query.ExecuteWithoutResultsAsync();
            await context.Posts.AddAsync(post);
            await context.SaveChangesAsync();
        }

        public async Task AddUserToDBAsync(User obj)
        {
            // Needs to store the complete User in SQL
            // And only the Id of the User in Neo4j.
            if (context.Users.FirstOrDefault(n => n.userId == obj.userId) == null)
            {
                await context.Users.AddAsync(obj);
                await context.SaveChangesAsync();

                await graphobj.graph.Cypher
                .Create("(u:User)")
                .Set("u={obj}")
                .WithParams(new
                {
                    id = obj.userId,
                    obj
                })
                .ExecuteWithoutResultsAsync();
            }

        }

        public async Task AddCommentToDBAsync(Comment comment)
        {
            // Needs to store all the comments in SQL
            // Needs to store only Id in Neo4j
            // Also needs to create relationships
            // between comment->post, authorOfComment(user)->comment.

            // Also needs to produce in RabbitMQ.
            // To produce in RabbitMQ it needs set of 
            // interested users which needs to be fetched
            // from Neo4j.

            var query = graphobj.graph.Cypher
               .Match("(u:User)", "(p:Post)")
               .Where((User u) => u.userId == comment.userId)
               .AndWhere((Post p) => p.postId == comment.postId)
               .Create("(u)-[:writes]->(c:Comment {comment})-[:onPost]->(p)")
               .WithParams(new
               {
                   comment
               });
            Console.WriteLine(query.Query.QueryText);
            await query.ExecuteWithoutResultsAsync();
            await context.Comments.AddAsync(comment);
            await context.SaveChangesAsync();

            Notification notification = new Notification();
            notification.Message = comment.userId+"is commented on your post";
            notification.TargetUrl = "http://172.23.238.164:5002/api/posts/"+comment.postId;
            // Task<List<string>> Temp  = System.Threading.Tasks.Task<List<string>>.Run(() => GetUsersAsync(comment.postId).Result) ;
            List<string> listOfUsers = await GetUsersAsync(comment.postId);
            notification.Users = listOfUsers;
        }

        public async Task<List<string>> GetUsersAsync(int postId)
        {
            List<string> users = await context.Posts.Where(p => p.postId == postId).Select(u => u.userId).ToListAsync();
            return users;
        }

        public void Dispose() 
        {
            context.Dispose();
        }
 
    }
}

/*
        public List<Topic> GetAllTopicImage()
        {
             var userFaker = new Faker<Topic>()
            .RuleFor(t => t.topic_image, f => f.Image.People());
            .RuleFor(t => t.topic_image, f => f.Internet.Avatar());
            var users = userFaker.Generate(1);
            return users;
        }
        
        public List<Topic> GetAllTopicName()
        {
             var userFaker1 = new Faker<Topic>()
            .RuleFor(t => t.topic_name, f => f.Name.FirstName());
            var myusers = userFaker1.Generate(1);
            return myusers;
        }

        public List<Post> GetAllPost()
        {
             var userFaker2 = new Faker<Post>()
            .RuleFor(t => t.posts, f => f.Lorem.Sentence());
            var myusers = userFaker2.Generate(1);
            return myusers;
        }
        */

// public List<UserC> GetAllUserImage()
// {
//     var userFaker3 = new Faker<UserC>()
//     .RuleFor(t => t.topic_image, f => f.Image.People());
//     .RuleFor(t => t.user_image, f => f.Internet.Avatar());
//     var users = userFaker3.Generate(1);
//     return users;
// }

// public List<UserC> GetAllUserName()
// {
//     var userFaker4 = new Faker<UserC>()
//     .RuleFor(t => t.user_name, f => f.Name.FirstName());
//     var myusers = userFaker4.Generate(1);
//     return myusers;
// }

//  public async Task<List<string>> fetchTopicAsync()
// {
//      string topicUrl = "http://172.23.238.164:8080/api/quizrt/topics";
//     //The 'using' will help to prevent memory leaks.
//     //Create a new instance of HttpClient
//     List<string> lg = new List<string>();
//      using (HttpClient client = new HttpClient())

//     //Setting up the response...         
//     using (HttpResponseMessage res = await client.GetAsync(topicUrl))
//     using (HttpContent content = res.Content)
//     {
//         string data = await content.ReadAsStringAsync();
//        // Console.WriteLine(data+"prateeeeeeeeeeeeeeeeeeeeeeek");
//         //data = data.Trim( new Char[] { '[',']' } );
//          JArray json = JArray.Parse(data);
//          // Console.WriteLine(json+"jkfsfjksfjsjfhskhfks");
//         // string ret;
//         // if (data != null)
//         // {
//         //     for(int i=0;i<json.Count;i++)
//         //     {
//         //         ret=(string)json[i]["topicName"];
//         //         if(!(lg.Contains(ret)))
//         //         lg.Add(ret);
//         //     }
//         // }
//         string value;
//         if(data != null){
//             for(int i = 0;i < json.Count; i++)
//             {
//                 value = (string)json[i];
//                 if(!(lg.Contains(value)))
//                 {
//                     lg.Add(value);
//                 }
//             //    Console.WriteLine(json[i]);
//             }
//         }
//         return lg;
//     }
// }

// List<List<Post>> x= await context.Topics
//        .Where(t => t.topicName == topicName)
//        .Include("posts").Select(s =>s.posts)
//        .Include("comments")
//        .ToListAsync();
// Console.WriteLine(x+"asdadsasddsa");

// public void GetTopicsFromRabbitMQ()
// {
//     var factory = new ConnectionFactory() { HostName = "192.168.176.4", UserName = "rabbitmq", Password = "rabbitmq" };
//     using (var connection = factory.CreateConnection())
//     using (var channel = connection.CreateModel())
//     {
//         channel.QueueDeclare(queue: "Topic", durable: false, exclusive: false, autoDelete: false, arguments: null);

//         var consumer = new EventingBasicConsumer(channel);
//         consumer.Received += (model, ea) =>
//         {
//             var body = ea.Body;
//             var message = Encoding.UTF8.GetString(body);
//             Console.WriteLine(" [x] Received {0}", message);
//         };
//         channel.BasicConsume(queue: "Topic", autoAck: true, consumer: consumer);

//         Console.WriteLine(" Press [enter] to exit.");
//         Console.ReadLine();
//     }
// }
