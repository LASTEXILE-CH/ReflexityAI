﻿using System;
using System.Collections.Generic;
using System.Linq;
using Plugins.ReflexityAI.MainNodes;
using UnityEditor;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Plugins.ReflexityAI.Framework {
    public enum ResolutionType { Robotic, Human }
    public enum InteractionType { Cooperative, Competitive }
    public class ReflexityAI : MonoBehaviour {

        [Tooltip("Brains copied by the AI")]
        public List<AIBrainGraph> AIBrains;
        [Tooltip("If checked the AI will be processed on enable")]
        public bool OnEnableQueuing = true;
        [Tooltip("If checked the AI will be automatically processed in loop")]
        public bool QueuingLoop = true;
        [Tooltip("Robotic : Always pick best option\n" +
                 "Human : Randomize between best options")]
        public ResolutionType OptionsResolution = ResolutionType.Robotic;
        [Tooltip("Cooperative : Each best option per brain are executed\n" +
                 "Competitive : The best option for all brain is executed")]
        public InteractionType MultiBrainInteraction = InteractionType.Cooperative;
        [Tooltip("Brains used by the AI")]
        public List<AIBrainGraph> LocalAIBrains;
        
        public readonly Dictionary<AIBrainGraph, List<AIOption>> WeightedOptions = new Dictionary<AIBrainGraph, List<AIOption>>();
        public readonly Dictionary<AIBrainGraph, List<AIOption>> BestWeightedOptions = new Dictionary<AIBrainGraph, List<AIOption>>();
        public readonly Dictionary<AIBrainGraph, AIOption> SelectedOptions = new Dictionary<AIBrainGraph, AIOption>();

        private bool _isInQueue;
        private readonly Dictionary<string, object> _memory = new Dictionary<string, object>();
        private readonly Dictionary<string, float> _historic = new Dictionary<string, float>();

        private void Start() {
            foreach (AIBrainGraph aiBrain in AIBrains) {
                // Create a copy
                AIBrainGraph localAIBrain = (AIBrainGraph) aiBrain.Copy();
                localAIBrain.name = aiBrain.name + "Of" + gameObject.name;
                // Setup Contexts
                foreach (IContextual contextual in localAIBrain.GetNodes<IContextual>()) {
                    contextual.Context = this;
                }
                LocalAIBrains.Add(localAIBrain);
            }
        }

        private void OnEnable() {
            if (!OnEnableQueuing) return;
            EnqueueAI();
        }

        /// <summary>
        /// Use this method to manually enqueue this AI processing
        /// </summary>
        [ContextMenu("Enqueue AI")]
        public void EnqueueAI() {
            AIQueue.Queue.Enqueue(this);
            if (!AIQueue.IsQueuing) StartCoroutine(AIQueue.Queuing());
        }

        /// <summary>
        /// Stop the AI queue from processing
        /// </summary>
        [ContextMenu("Stop Queue")]
        public static void StopQueue() {
            AIQueue.IsQueuing = false;
        }

        /// <summary>
        /// Clear all process from the AI queue
        /// </summary>
        [ContextMenu("Clear Queue")]
        public static void ClearQueue() {
            AIQueue.Queue.Clear();
        }

        internal void ThinkAndAct() {
            WeightedOptions.Clear();
            foreach (AIBrainGraph aiBrain in LocalAIBrains.Where(brainGraph => brainGraph != null)) {
                // Get all options from all brains and calculate Weights
                WeightedOptions.Add(aiBrain, GetOptions(aiBrain));
            }
            // Fetch best options according to multi brain interaction
            BestOptionsOnWeight(WeightedOptions, BestWeightedOptions);
            // Check if weight are not enough to start execution
            if (IsWeightEnoughForSelection(BestWeightedOptions)) {
                SelectedOptions.Clear();
                foreach (KeyValuePair<AIBrainGraph,List<AIOption>> valuePair in WeightedOptions) {
                    foreach (AIOption aiOption in valuePair.Value) {
                        SelectedOptions.Add(valuePair.Key, aiOption);
                    }
                }
            }
            // Else calculate options rank
            else {
                // Calculate options rank
                foreach (KeyValuePair<AIBrainGraph,List<AIOption>> valuePair in BestWeightedOptions) {
                    foreach (AIOption aiOption in valuePair.Value) {
                        foreach (ICacheable cacheable in valuePair.Key.GetNodes<ICacheable>()) {
                            cacheable.ClearShortCache();
                        }
                        aiOption.CalculateRank();
                    }
                }
                // Calculate overall ranks
                CalculateOverallRanks(BestWeightedOptions);
                // Fetch selected options according to multi brain interaction and options resolution
                SelectOptionsOnOverallRank(BestWeightedOptions, SelectedOptions);
            }
            // Order options for display
            foreach (KeyValuePair<AIBrainGraph,List<AIOption>> valuePair in WeightedOptions.ToList()) {
                WeightedOptions[valuePair.Key] = valuePair.Value
                    .OrderByDescending(option => option.Weight)
                    .ThenByDescending(option => option.Rank).ToList();
            }
            // Execute selected options
            foreach (KeyValuePair<AIBrainGraph,AIOption> selectedOption in SelectedOptions) {
                selectedOption.Value.ExecuteActions();
            }
        }

        private List<AIOption> GetOptions(AIBrainGraph aiBrain) {
            List<AIOption> aiOptions = new List<AIOption>();
            foreach (ICacheable cacheable in aiBrain.GetNodes<ICacheable>()) {
                cacheable.ClearCache();
            }
            foreach (OptionNode optionNode in aiBrain.GetNodes<OptionNode>()) {
                aiOptions.AddRange(optionNode.GetOptions());
            }
            return aiOptions;
        }

        private void BestOptionsOnWeight(Dictionary<AIBrainGraph,List<AIOption>> options, Dictionary<AIBrainGraph,List<AIOption>> bestOptions) {
            bestOptions.Clear();
            switch (MultiBrainInteraction) {
                case InteractionType.Cooperative: {
                    // Cooperative then take best weight of each brain
                    foreach (KeyValuePair<AIBrainGraph,List<AIOption>> valuePair in options.Where(pair => pair.Value.Count > 0)) {
                        int maxWeight = valuePair.Value.Max(option => option.Weight);
                        if (maxWeight == 0) continue;
                        bestOptions.Add(valuePair.Key, valuePair.Value.Where(option => option.Weight == maxWeight).ToList());
                    }
                    break;
                }
                case InteractionType.Competitive: {
                    // Competitive then take best weight from all brain
                    int maxWeight = options.Max(pair => pair.Value.Max(option => option.Weight));
                    if (maxWeight == 0) return;
                    foreach (KeyValuePair<AIBrainGraph,List<AIOption>> valuePair in options.Where(pair => pair.Value.Count > 0)) {
                        List<AIOption> aiOptions = valuePair.Value.Where(option => option.Weight == maxWeight).ToList();
                        if (aiOptions.Count > 0) bestOptions.Add(valuePair.Key, aiOptions);
                    }
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private bool IsWeightEnoughForSelection(Dictionary<AIBrainGraph,List<AIOption>> options) {
            switch (MultiBrainInteraction) {
                case InteractionType.Cooperative:
                    if (options.Any(valuePair => valuePair.Value.Count > 1)) return false; 
                    break;
                case InteractionType.Competitive:
                    if (options.Sum(pair => pair.Value.Count) > 1) return false;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return true;
        }

        private void CalculateOverallRanks(Dictionary<AIBrainGraph, List<AIOption>> options) {
            int minRank = options.Min(pair => pair.Value.Min(option => option.Rank));
            foreach (AIOption option in options.SelectMany(valuePair => valuePair.Value)) {
                option.OverallRank = option.Rank - minRank + 1;
            }
        }

        private void SelectOptionsOnOverallRank(Dictionary<AIBrainGraph, List<AIOption>> options, Dictionary<AIBrainGraph, AIOption> selectedOptions) {
            selectedOptions.Clear();
            switch (OptionsResolution) {
                // Calculate probability
                case ResolutionType.Human when MultiBrainInteraction == InteractionType.Competitive: {
                    // Calculate relative probability from all brain options
                    float rndProbability = Random.Range(0f, 1f);
                    float probabilitySum = 0;
                    float overallRankSum = options.Sum(pair => pair.Value.Sum(option => option.OverallRank));
                    foreach (KeyValuePair<AIBrainGraph,List<AIOption>> valuePair in options) {
                        foreach (AIOption aiOption in valuePair.Value) {
                            aiOption.Probability = aiOption.OverallRank / overallRankSum;
                            probabilitySum += aiOption.Probability;
                            if (selectedOptions.Count == 0 && probabilitySum >= rndProbability) {
                                selectedOptions.Add(valuePair.Key, aiOption);
                            }
                        }
                    }
                    break;
                }
                case ResolutionType.Human when MultiBrainInteraction == InteractionType.Cooperative: {
                    // Calculate relative probability from same brain options
                    foreach (KeyValuePair<AIBrainGraph,List<AIOption>> valuePair in options) {
                        float rndProbability = Random.Range(0f, 1f);
                        float probabilitySum = 0;
                        float overallRankSum = options.Sum(pair => pair.Value.Sum(option => option.OverallRank));
                        foreach (AIOption aiOption in valuePair.Value) {
                            aiOption.Probability = aiOption.OverallRank / overallRankSum;
                            probabilitySum += aiOption.Probability;
                            if (!selectedOptions.ContainsKey(valuePair.Key) && probabilitySum >= rndProbability) {
                                selectedOptions.Add(valuePair.Key, aiOption);
                            }
                        }
                    }
                    break;
                }
                case ResolutionType.Robotic when MultiBrainInteraction == InteractionType.Competitive: {
                    // Calculate absolute probability from all brain options
                    float maxOverallRank = options.Max(pair => pair.Value.Max(option => option.OverallRank));
                    foreach (KeyValuePair<AIBrainGraph,List<AIOption>> valuePair in options) {
                        foreach (AIOption aiOption in valuePair.Value) {
                            if (SelectedOptions.Count == 0 && aiOption.OverallRank >= maxOverallRank) {
                                aiOption.Probability = 1;
                                SelectedOptions.Add(valuePair.Key, aiOption);
                            } else {
                                aiOption.Probability = 0;
                            }
                        }
                    }
                    break;
                }
                case ResolutionType.Robotic when MultiBrainInteraction == InteractionType.Cooperative:
                    // Calculate absolute probability from same brain options
                    foreach (KeyValuePair<AIBrainGraph,List<AIOption>> valuePair in options) {
                        foreach (AIOption aiOption in valuePair.Value) {
                            float maxOverallRank = valuePair.Value.Max(option => option.OverallRank);
                            if (!SelectedOptions.ContainsKey(valuePair.Key) && aiOption.OverallRank >= maxOverallRank) {
                                aiOption.Probability = 1;
                                SelectedOptions.Add(valuePair.Key, aiOption);
                            } else {
                                aiOption.Probability = 0;
                            }
                        }
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        // Memory

        public void SaveInMemory(string memoryTag, object data) {
            if (_memory.ContainsKey(memoryTag)) _memory[memoryTag] = data;
            else _memory.Add(memoryTag, data);
        }

        public object LoadFromMemory(string memoryTag) {
            return _memory.ContainsKey(memoryTag) ? _memory[memoryTag] : null;
        }
        
        public void ClearFromMemory(string memoryTag) {
            _memory.Remove(memoryTag);
        }
        
        // Historic

        public void SaveInHistoric(string historicTag) {
            if (_historic.ContainsKey(historicTag)) _historic[historicTag] = Time.realtimeSinceStartup;
            else _historic.Add(historicTag, Time.realtimeSinceStartup);
        }

        public float HistoricTime(string historicTag) {
            return _historic.ContainsKey(historicTag) ? _historic[historicTag] : 0;
        }

    }
}