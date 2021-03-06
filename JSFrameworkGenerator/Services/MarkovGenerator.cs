﻿using StandaloneApp.JSFrameworkGenerator.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StandaloneApp.JSFrameworkGenerator.Services
{
    using MarkovTable = IReadOnlyDictionary<Prefix, SuffixFrequency[]>;

    /// <summary>
    /// Generates markov chain
    /// https://en.wikipedia.org/wiki/Markov_chain#Markov_text_generators
    /// </summary>
    sealed class MarkovGenerator
    {
        // datastructure representing prefixes and weighted suffixes
        private readonly MarkovTable frequencies;
        private readonly Func<int, int, int> RandomInt;

        /// <summary>
        /// Create a new markov generator.
        /// </summary>
        /// <param name="tokens">source sequence of tokens to analyze</param>
        /// <param name="ngramSize">
        ///     The ngram size, i.e. how many consecutive tokens to group by. Larger values
        ///     result in more similarity to the <paramref name="tokens"/> source.
        ///     See https://en.wikipedia.org/wiki/N-gram
        /// </param>
        /// <param name="randomInt">
        ///     A function that produces a random int between two bounds.
        ///     If not provided, <see cref="Random.Next(int, int)"/> is used.
        ///     This parameter is intended to be used mostly in unit testing.
        /// </param>
        public MarkovGenerator(IReadOnlyCollection<string> tokens, int ngramSize, Func<int, int, int> randomInt = null)
        {
            frequencies = BuildMarkovTable(tokens, ngramSize);
            RandomInt = randomInt ?? new Random().Next;
        }

        /// <summary>
        /// Generates a markov chain.
        /// </summary>
        /// <returns>A markov sequence of the tokens. The enumerable may be infinite.</returns>
        public IEnumerable<string> Generate()
        {
            Prefix initial = PickRandomKey(frequencies);
            IEnumerable<string> generated = RandomWalk(frequencies, initial);
            return generated;
        }

        /// <summary>
        /// Builds the <paramref name="tokens"/> into the main <see cref="MarkovTable"/> datastructure.
        /// </summary>
        /// <remarks>
        /// given the <paramref name="tokens"/> [a b c a b x] and an <paramref name="ngramSize"/> of 2,
        /// the result would be:
        /// {
        ///    [a b]: [c:1, x:1],
        ///    [b c]: [a:1],
        ///    [c a]: [b:1]
        /// }
        /// </remarks>
        private static MarkovTable BuildMarkovTable(IReadOnlyCollection<string> tokens, int ngramSize)
        {
            return tokens
                // create a sliding window over the tokens
                .Buffer(ngramSize + 1, 1)
                // ignore incomplete windows generated by Buffer (from tokens at the end of the array)
                .Where(segment => segment.Count == ngramSize + 1)
                .GroupBy(
                    // group by the first `ngramSize` elements of the window
                    segment => new Prefix(segment.Take(ngramSize).ToArray()),
                    // group members are the last element of each window
                    segment => segment.Last()
                )
                // transform group members into a frequency table.
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .GroupBy(element => element)
                        .Select(element => new SuffixFrequency(element.Key, element.Count()))
                        .ToArray()
                );
        }

        private Prefix PickRandomKey(MarkovTable frequencies)
        {
            var candidates = frequencies.Keys
                .Where(key => char.IsUpper(key.Elements[0][0]))
                .ToList();
            int startIndex = RandomInt(0, candidates.Count);
            Prefix current = candidates.ElementAt(startIndex);
            return current;
        }

        /// <summary>
        /// Given a <see cref="MarkovTable"/> and an initial starting <see cref="Prefix"/>, looks up the
        /// prefix in the MarkovTable to construct a new prefix. This process continues until we reach
        /// the end of the original tokens (if ever!).
        /// </summary>
        private IEnumerable<string> RandomWalk(MarkovTable frequencies, Prefix initial)
        {
            return EnumerableEx.Generate(
                initialState: initial,
                condition: prefix => !prefix.Elements.All(e => e == null),
                iterate: prefix =>
                {
                    string suffix = ChooseRandomSuffix(frequencies, prefix);
                    // slide suffix onto old prefix to create new prefix
                    var newPrefix = prefix.Elements.Skip(1).Append(suffix).ToArray();
                    return new Prefix(newPrefix);
                },
                resultSelector: prefixes => prefixes.Elements.First()
            );
        }

        /// <summary>
        /// Look up the possible suffixes in <paramref name="frequencies"/> for the <paramref name="current"/> prefix.
        /// Each suffix has a weight -- randomly choose one based on the weighting.
        /// </summary>
        /// <returns>a randomly chosen string suffix</returns>
        private string ChooseRandomSuffix(MarkovTable frequencies, Prefix current)
        {
            if(!frequencies.TryGetValue(current, out SuffixFrequency[] nextFrequencies))
            {
                // we've chosen a branch that leads to the end of the tokens.
                return null;
            }
            int total = nextFrequencies.Sum(freq => freq.Frequency);
            var random = RandomInt(0, total);
            return nextFrequencies
                .SkipWhile(frequency =>
                {
                    random -= frequency.Frequency;
                    return random > 0;
                })
                .First()
                .Suffix;
        }
    }
}