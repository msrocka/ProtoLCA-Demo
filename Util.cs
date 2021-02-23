﻿using System;
using System.Text;
using System.Security.Cryptography;

using Google.Protobuf.Collections;

namespace DemoApp
{
    public static class Util
    {

        public static int MatchLengthOf(this string s, params string[] words)
        {
            if (s == null || words == null || words.Length == 0)
            {
                return 0;
            }
            var feed = s.ToLowerInvariant();
            int count = 0;
            foreach (var word in words)
            {
                if (string.IsNullOrWhiteSpace(word))
                    continue;
                var w = word.ToLowerInvariant();
                if (feed.Contains(w))
                {
                    count += w.Length;
                }
            }
            return count;
        }

        public static string Join(this RepeatedField<string> f, string separator)
        {
            if (f == null || f.Count == 0)
                return "";
            var builder = new StringBuilder();
            for (int i = 0; i < f.Count; i++)
            {
                if (i != 0)
                {
                    builder.Append(separator);
                }
                builder.Append(f[i]);
            }
            return builder.ToString();
        }

        public static string MakeID(params string[] args)
        {
            var path = new StringBuilder();
            for (int i = 0; i < args.Length; i++)
            {
                var part = args[i] == null
                    ? ""
                    : args[i].Trim().ToLower();
                if (i != 0)
                {
                    path.Append("/");
                }
                path.Append(part);
            }
            var hash = MD5.Create().ComputeHash(
                Encoding.UTF8.GetBytes(path.ToString()));
            return new Guid(hash).ToString();
        }

        public static string OrEmpty(this string s)
        {
            return s == null ? "" : s.Trim();
        }

        public static string IfEmpty(this string s, string t)
        {
            return String.IsNullOrWhiteSpace(s)
                ? t
                : s.Trim();
        }

        public static bool EqualsIgnoreCase(this string s, string other)
        {
            if (s == null && other == null)
                return true;
            if (s == null || other == null)
                return false;
            var sl = s.Trim().ToLowerInvariant();
            var ol = other.Trim().ToLowerInvariant();
            return sl.Equals(ol);
        }

        public static bool IsEmpty(this string s)
        {
            return String.IsNullOrWhiteSpace(s);
        }

        public static bool IsNotEmpty(this string s)
        {
            return !String.IsNullOrWhiteSpace(s);
        }

        public static void Log(string s)
        {
            Console.WriteLine(s);
        }

        public static string Repeat(this string s, int n)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                builder.Append(s);
            }
            return builder.ToString();
        }

    }
}
