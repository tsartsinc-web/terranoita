using System.Collections.Generic;
using System.Linq;
using Terranoita.Noita;
using Xunit;

namespace Terranoita.Tests
{
    public class NoitaProbeTests
    {
        // lines as the probe wrote them in Noita on 2026-10-09 (single:BOMB shortened to its fields)
        const string Lines =
            "{\"name\":\"diag:S5\",\"deck\":[\"LIGHT_BULLET\"],\"fire_method\":2,\"mana_used\":0.00,\"frames\":31,\"projectiles\":[],\"hits\":[]}\n" +
            "{\"name\":\"single:LIGHT_BULLET\",\"deck\":[\"LIGHT_BULLET\"],\"fire_method\":2,\"mana_used\":5.00,\"frames\":44,\"target_alive\":true," +
            "\"note\":\"fired at frame 1; S5\",\"projectiles\":[{\"file\":\"data/entities/projectiles/deck/light_bullet.xml\",\"parent\":\"\"," +
            "\"born\":1,\"end\":14,\"lifetime\":46.00,\"x0\":22.70,\"y0\":-5.95,\"vx0\":730.98,\"vy0\":-15.04,\"vx1\":710.27,\"vy1\":-11.37," +
            "\"x1\":144.58,\"y1\":-4.65}],\"hits\":[{\"damage\":0.12,\"message\":\"$damage_projectile\",\"by\":\"data/entities/projectiles/deck/light_bullet.xml\"}]}\n" +
            "{\"name\":\"single:BOMB\",\"deck\":[\"BOMB\"],\"mana_used\":25.00,\"frames\":180,\"projectiles\":[{\"file\":\"data/entities/projectiles/bomb.xml\"," +
            "\"parent\":\"\",\"born\":1,\"end\":null,\"lifetime\":null,\"x0\":10.5,\"y0\":-5,\"vx0\":120,\"vy0\":-3,\"vx1\":null,\"vy1\":null,\"x1\":102.1,\"y1\":4.3}]," +
            "\"hits\":[{\"damage\":5.00,\"message\":\"say \\\"boom\\\"\\n\",\"by\":\"\"}]}\n" +
            "{\"done\":true,\"tests\":875}\n";

        [Fact]
        public void ReadsTheProbesLines()
        {
            var rows = NoitaProbe.Read(Lines);
            Assert.Equal(new[] { "single:LIGHT_BULLET", "single:BOMB" }, rows.Select(r => r.Name));   // diag and done lines skipped
            var spark = rows[0];
            Assert.Equal(new[] { "LIGHT_BULLET" }, spark.Deck);
            Assert.Equal(5.0, spark.ManaUsed);
            var shot = spark.Shots.Single();
            Assert.Equal(("data/entities/projectiles/deck/light_bullet.xml", "", 1, (int?)14), (shot.File, shot.Parent, shot.Born, shot.End));
            Assert.Equal(731.13, shot.Speed0, 2);
            Assert.Equal(0.12, spark.Hits.Single().Damage);
            Assert.True(spark.Fired);

            var bomb = rows[1];
            Assert.Null(bomb.Shots[0].End);                 // still flying when the test ended
            Assert.Null(bomb.Shots[0].Vx1);
            Assert.Equal("say \"boom\"\n", bomb.Hits[0].Message);
        }

        [Fact]
        public void MiniJsonValues()
        {
            var o = (Dictionary<string, object>)MiniJson.Parse("{ \"a\" : [1, -2.5e1, true, false, null, \"x\\u0041\"], \"b\": {} }");
            Assert.Equal(new object[] { 1.0, -25.0, true, false, null, "xA" }, (List<object>)o["a"]);
            Assert.Empty((Dictionary<string, object>)o["b"]);
            Assert.Throws<System.FormatException>(() => MiniJson.Parse("{\"a\":1} x"));
        }
    }
}
