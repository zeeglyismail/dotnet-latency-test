using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text.Json;
using TestProj.Models;
using TestProj.Middleware;  // <-- IMPORTANT: include this

namespace TestProj.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RedisController : ControllerBase
    {
        private readonly IDatabase _redisDb;

        public RedisController(IConnectionMultiplexer redis)
        {
            _redisDb = redis.GetDatabase();
        }

        // POST: api/Redis
        [HttpPost]
        public async Task<ActionResult<DBEntity>> PostRedisEntitiy([FromBody] DBEntity dBEntity)
        {
            dBEntity.Id = Guid.NewGuid(); // Ensure unique IDs

            var hashEntry = new HashEntry[]
            {
                new HashEntry($"{dBEntity.Id}", dBEntity.Name)
            };

            // 🔥 Deep state: Redis write
            RequestStateHelper.SetState(HttpContext, "WaitingForRedis");

            await _redisDb.HashSetAsync($"entity", hashEntry);

            return CreatedAtAction("GetDBEntity", new { id = dBEntity.Id }, dBEntity);
        }

        // POST: api/Redis/100
        [HttpPost("{count}")]
        public async Task<ActionResult<DBEntity>> PostRedisBulkInsert(int count)
        {
            List<Task> redisTasks = new List<Task>();

            for (int i = 0; i < count; i++)
            {
                var _temp = new DBEntity
                {
                    Id = Guid.NewGuid(),
                    Name = $"Name_{i}_{Guid.NewGuid()}"
                };

                string jsonEntity = JsonSerializer.Serialize(_temp);

                // 🔥 Mark Redis wait (inside the loop or before await)
                RequestStateHelper.SetState(HttpContext, "WaitingForRedis");

                redisTasks.Add(_redisDb.StringSetAsync(
                    $"entity:{_temp.Id}",
                    jsonEntity,
                    TimeSpan.FromMinutes(500)
                ));

                redisTasks.Add(_redisDb.StringSetAsync(
                    $"entity:name:{_temp.Name}",
                    jsonEntity,
                    TimeSpan.FromMinutes(500)
                ));
            }

            // 🔥 Bulk Redis writes happening
            RequestStateHelper.SetState(HttpContext, "WaitingForRedis");

            await Task.WhenAll(redisTasks);

            // Handler is finishing, so state becomes ExecutingHandler again.
            RequestStateHelper.SetState(HttpContext, "ExecutingHandler");

            return Ok(new { Message = "Batch insert successful" });
        }

        // GET: api/Redis/count
        [HttpGet("count")]
        public async Task<IActionResult> GetRedisEntityCount()
        {
            try
            {
                // 🔥 Mark Redis read
                RequestStateHelper.SetState(HttpContext, "WaitingForRedis");

                var server = _redisDb.Multiplexer.GetServer(
                    _redisDb.Multiplexer.GetEndPoints().First()
                );

                var keys = server.Keys(pattern: "entity:*").Count();

                return Ok(new { RedisCount = keys });
            }
            catch (Exception ex)
            {
                RequestStateHelper.SetState(HttpContext, "RedisError");
                throw;
            }
        }
    }
}
