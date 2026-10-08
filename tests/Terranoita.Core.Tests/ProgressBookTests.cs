using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terranoita.Progress;
using Xunit;

namespace Terranoita.Tests
{
    public class ProgressBookTests
    {
        [Fact]
        public void SeeAndCount()
        {
            var b = new ProgressBook();
            var news = new List<string>();
            b.Discovered += (c, id) => news.Add(c + ":" + id);
            Assert.True(b.See(ProgressBook.Spells, "LIGHT_BULLET"));
            Assert.False(b.See(ProgressBook.Spells, "LIGHT_BULLET"));
            b.Count(ProgressBook.Creatures, "zombie", "kills");
            b.Count(ProgressBook.Creatures, "zombie", "kills", 2);
            Assert.Equal(3, b.CountOf(ProgressBook.Creatures, "zombie", "kills"));
            Assert.Equal(0, b.CountOf(ProgressBook.Creatures, "zombie", "seen_times"));
            Assert.Equal(new[] { "spell:LIGHT_BULLET", "creature:zombie" }, news);   // counting marks it as met too, once
            Assert.Equal(1, b.KnownCount(ProgressBook.Spells));
            Assert.True(b.Dirty);
        }

        [Fact]
        public void PageKeepsTheGamesOrderAndUnknownOnes()
        {
            var b = new ProgressBook();
            b.See(ProgressBook.Spells, "BOMB");
            b.See(ProgressBook.Spells, "OLD_REMOVED_SPELL");
            var page = b.Page(ProgressBook.Spells, new[] { "LIGHT_BULLET", "BOMB", "BOMB", "SPARK" });
            Assert.Equal(new[] { ("LIGHT_BULLET", false), ("BOMB", true), ("SPARK", false), ("OLD_REMOVED_SPELL", true) }, page);
        }

        [Fact]
        public void TextRoundTripAndBadLines()
        {
            var b = new ProgressBook();
            b.Count(ProgressBook.Spells, "BOMB", "casts", 12);
            b.See(ProgressBook.Creatures, "hiisi\tbad name");
            var text = b.Write();
            var c = ProgressBook.Read(text + "garbage line\nspell\t\nspell\tSPARK\tcasts=notanumber\n");
            Assert.Equal(12, c.CountOf(ProgressBook.Spells, "BOMB", "casts"));
            Assert.True(c.Has(ProgressBook.Creatures, "hiisi bad name"));
            Assert.True(c.Has(ProgressBook.Spells, "SPARK"));
            Assert.Equal(0, c.CountOf(ProgressBook.Spells, "SPARK", "casts"));
            Assert.Equal(2, c.KnownCount(ProgressBook.Spells));
            Assert.False(c.Dirty);
        }

        [Fact]
        public void SavesAtomicallyPerCharacter()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tn_progress_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                string a = ProgressBook.FileFor(dir, Path.Combine(dir, "Players", "Hero.plr"), "Hero");
                string b2 = ProgressBook.FileFor(dir, null, "Hero:2");
                Assert.EndsWith("progress_Hero.txt", a);
                Assert.EndsWith("progress_Hero2.txt", b2);
                var book = new ProgressBook();
                book.See(ProgressBook.Spells, "BOMB");
                book.Save(a);
                book.Count(ProgressBook.Spells, "BOMB", "casts");
                book.Save(a);                                  // over an existing file
                Assert.False(book.Dirty);
                Assert.False(File.Exists(a + ".tmp"));
                var back = ProgressBook.Load(a);
                Assert.Equal(1, back.CountOf(ProgressBook.Spells, "BOMB", "casts"));
                Assert.Equal(0, ProgressBook.Load(Path.Combine(dir, "none.txt")).KnownCount(ProgressBook.Spells));
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }
    }
}
