using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Unite.Kernel
{
    /// <summary>
    /// Owns the deterministic random seed for one study run.
    /// </summary>
    public static class StudyRandom
    {
        public static string ID { get; private set; }
        public static int Seed { get; private set; }
        public static bool IsInitialized { get; private set; }

        /// <summary>
        /// Seeds Unity's global pseudo-random generator from a researcher-defined ID.
        /// The same exact ID always produces the same seed on every platform.
        /// </summary>
        public static void Initialize(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "A non-empty study ID is required for deterministic randomization.",
                    nameof(id));
            }

            ID = id.Trim();
            Seed = DeriveSeed(ID);
            IsInitialized = true;
            UnityEngine.Random.InitState(Seed);
        }

        public static int SeedFromID(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "A non-empty study ID is required.",
                    nameof(id));
            }

            return DeriveSeed(id.Trim());
        }

        private static int DeriveSeed(string id)
        {
            string input = $"UNITE\nID:{id.Length}:{id}";

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));

                // Explicit byte order keeps the result stable across architectures.
                return hash[0] |
                       (hash[1] << 8) |
                       (hash[2] << 16) |
                       (hash[3] << 24);
            }
        }
    }
}
