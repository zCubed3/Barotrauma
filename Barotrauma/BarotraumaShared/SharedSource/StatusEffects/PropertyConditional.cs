﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using System.Linq;

namespace Barotrauma
{
    // TODO: This class should be refactored: 
    // - Use XElement instead of XAttribute in the constructor
    // - Simplify, remove unnecessary conversions
    // - Improve the flow so that the logic is undestandable.
    // - Maybe add some test cases for the operators?
	public class PropertyConditional
    {
        public enum ConditionType
        {
            PropertyValue,
            Name,
            SpeciesName,
            SpeciesGroup,
            HasTag,
            HasStatusTag,
            Affliction,
            EntityType,
            LimbType
        }

        public enum Comparison
        {
            And,
            Or
        }

        public enum OperatorType
        {
            Equals,
            NotEquals,
            LessThan,
            LessThanEquals,
            GreaterThan,
            GreaterThanEquals,
            None
        }

        public readonly ConditionType Type;
        public readonly OperatorType Operator;
        public readonly string AttributeName;
        public readonly string AttributeValue;
        public readonly string[] SplitAttributeValue;
        public readonly float? FloatValue;

        public readonly string TargetItemComponentName;

        // Only used by attacks
        public readonly bool TargetSelf;

        // Only used by conditionals targeting an item (makes the conditional check the item/character whose inventory this item is inside)
        public readonly bool TargetContainer;
        // Only used by conditionals targeting an item. By default, containers check the parent item. This allows you to check the grandparent instead.
        public readonly bool TargetGrandParent;

        // Remove this after refactoring
        public static bool IsValid(XAttribute attribute)
        {
            switch (attribute.Name.ToString().ToLowerInvariant())
            {
                case "targetitemcomponent":
                case "targetself":
                case "targetcontainer":
                    return false;
                default:
                    return true;
            }
        }

        // TODO: use XElement instead of XAttribute (how to do without breaking the existing content?)
        public PropertyConditional(XAttribute attribute)
        {
            AttributeName = attribute.Name.ToString().ToLowerInvariant();
            string attributeValueString = attribute.Value.ToString();
            if (string.IsNullOrWhiteSpace(attributeValueString))
            {
                DebugConsole.ThrowError($"Conditional attribute value is empty: {attribute.Parent.ToString()}");
                return;
            }
            string valueString = attributeValueString;
            string[] splitString = valueString.Split(' ');
            if (splitString.Length > 0)
            {
                for (int i = 1; i < splitString.Length; i++)
                {
                    valueString = splitString[i] + (i > 1 && i < splitString.Length ? " " : "");
                }
            }
            string op = splitString[0];
            OperatorType operatorType = GetOperatorType(op);

            if (operatorType != OperatorType.None)
            {
                Operator = operatorType;
            }
            else
            {
                if (op != "==" && op != "!=" && op != ">" && op != "<" && op != ">=" && op != "<=") //Didn't use escape strings or anything
                {
                    valueString = attributeValueString; //We probably don't even have an operator
                }
            }

            TargetItemComponentName = attribute.Parent.GetAttributeString("targetitemcomponent", "");
            TargetContainer = attribute.Parent.GetAttributeBool("targetcontainer", false);
            TargetSelf = attribute.Parent.GetAttributeBool("targetself", false);
            TargetGrandParent = attribute.Parent.GetAttributeBool("targetgrandparent", false);

            if (!Enum.TryParse(AttributeName, true, out Type))
            {
                if (AfflictionPrefab.Prefabs.Any(p => p.Identifier.Equals(AttributeName, StringComparison.OrdinalIgnoreCase)))
                {
                    Type = ConditionType.Affliction;
                }
                else
                {
                    Type = ConditionType.PropertyValue;
                }
            }
            
            AttributeValue = valueString;
            SplitAttributeValue = valueString.Split(',');
            if (float.TryParse(AttributeValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                FloatValue = value;
            }
        }

        public static OperatorType GetOperatorType(string op)
        {
            //thanks xml for not letting me use < or > in attributes :(
            switch (op)
            {
                case "e":
                case "eq":
                case "equals":
                    return OperatorType.Equals;
                case "ne":
                case "neq":
                case "notequals":
                case "!":
                case "!e":
                case "!eq":
                case "!equals":
                    return OperatorType.NotEquals;
                case "gt":
                case "greaterthan":
                    return OperatorType.GreaterThan;
                case "lt":
                case "lessthan":
                    return OperatorType.LessThan;
                case "gte":
                case "gteq":
                case "greaterthanequals":
                    return OperatorType.GreaterThanEquals;
                case "lte":
                case "lteq":
                case "lessthanequals":
                    return OperatorType.LessThanEquals;
                default:
                    return OperatorType.None;
            }
        }

        public bool Matches(ISerializableEntity target)
        {          
            switch (Type)
            {
                case ConditionType.PropertyValue:
                    SerializableProperty property;
                    if (target?.SerializableProperties == null) { return Operator == OperatorType.NotEquals; }
                    if (target.SerializableProperties.TryGetValue(AttributeName, out property))
                    {
                        return Matches(target, property);
                    }
                    return false;
                case ConditionType.Name:
                    if (target == null) { return Operator == OperatorType.NotEquals; }
                    return (Operator == OperatorType.Equals) == (target.Name == AttributeValue);
                case ConditionType.HasTag:
                    {
                        if (target == null) { return Operator == OperatorType.NotEquals; }
                        int matches = 0;
                        foreach (string tag in SplitAttributeValue)
                        {
                            if (target is Item item && item.HasTag(tag))
                            {
                                matches++;
                            }
                        }
                        //If operator is == then it needs to match everything, otherwise if its != there must be zero matches.
                        return Operator == OperatorType.Equals ? matches >= SplitAttributeValue.Length : matches <= 0;
                    }
                case ConditionType.HasStatusTag:
                    if (target == null) { return Operator == OperatorType.NotEquals; }
                    bool success = false;
                    if (StatusEffect.DurationList.Any(d => d.Targets.Contains(target)) || DelayedEffect.DelayList.Any(d => d.Targets.Contains(target)))
                    {
                        int matches = 0;
                        foreach (DurationListElement durationEffect in StatusEffect.DurationList)
                        {
                            if (!durationEffect.Targets.Contains(target)) { continue; }
                            foreach (string tag in SplitAttributeValue)
                            {
                                if (durationEffect.Parent.HasTag(tag))
                                {
                                    matches++;
                                }
                            }
                            success = Operator == OperatorType.Equals ? matches >= SplitAttributeValue.Length : matches <= 0;
                        }
                        foreach (DelayedListElement delayedEffect in DelayedEffect.DelayList)
                        {
                            if (!delayedEffect.Targets.Contains(target)) { continue; }
                            foreach (string tag in SplitAttributeValue)
                            {
                                if (delayedEffect.Parent.HasTag(tag))
                                {
                                    matches++;
                                }
                            }
                        }
                        return Operator == OperatorType.Equals ? matches >= SplitAttributeValue.Length : matches <= 0;
                    }
                    else if (Operator == OperatorType.NotEquals)
                    {
                        //no status effects, so the tags cannot be equal -> condition met
                        return true;
                    }
                    return success;
                case ConditionType.SpeciesName:
                    {
                        if (target == null) { return Operator == OperatorType.NotEquals; }
                        if (!(target is Character targetCharacter)) { return false; }
                        return Operator == OperatorType.Equals == targetCharacter.SpeciesName.Equals(AttributeValue, StringComparison.OrdinalIgnoreCase);
                    }
                case ConditionType.SpeciesGroup:
                    {
                        if (target == null) { return Operator == OperatorType.NotEquals; }
                        if (!(target is Character targetCharacter)) { return false; }
                        return Operator == OperatorType.Equals == targetCharacter.Params.CompareGroup(AttributeValue);
                    }
                case ConditionType.EntityType:
                    switch (AttributeValue)
                    {
                        case "character":
                        case "Character":
                            return (Operator == OperatorType.Equals) == target is Character;
                        case "limb":
                        case "Limb":
                            return (Operator == OperatorType.Equals) == target is Limb;
                        case "item":
                        case "Item":
                            return (Operator == OperatorType.Equals) == target is Item;
                        case "structure":
                        case "Structure":
                            return (Operator == OperatorType.Equals) == target is Structure;
                        case "null":
                            return (Operator == OperatorType.Equals) == (target == null);
                        default:
                            return false;
                    }
                case ConditionType.LimbType:
                    {
                        if (!(target is Limb limb))
                        {
                            return false;
                        }
                        else
                        {
                            return limb.type.ToString().Equals(AttributeValue, StringComparison.OrdinalIgnoreCase);
                        }
                    }
                case ConditionType.Affliction:
                    {
                        if (target == null) { return Operator == OperatorType.NotEquals; }

                        Character targetChar = target as Character;
                        if (target is Limb limb) { targetChar = limb.character; }
                        if (targetChar != null)
                        {
                            var health = targetChar.CharacterHealth;
                            if (health == null) { return false; }
                            var affliction = health.GetAffliction(AttributeName);
                            float afflictionStrength = affliction == null ? 0.0f : affliction.Strength;
                            if (FloatValue.HasValue)
                            {
                                float value = FloatValue.Value;
                                switch (Operator)
                                {
                                    case OperatorType.Equals:
                                        return afflictionStrength == value;
                                    case OperatorType.GreaterThan:
                                        return afflictionStrength > value;
                                    case OperatorType.GreaterThanEquals:
                                        return afflictionStrength >= value;
                                    case OperatorType.LessThan:
                                        return afflictionStrength < value;
                                    case OperatorType.LessThanEquals:
                                        return afflictionStrength <= value;
                                    case OperatorType.NotEquals:
                                        return afflictionStrength != value;
                                }
                            }
                        }
                    }
                    return false;
                default:
                    return false;
            }
        }
        
        // TODO: refactor and add tests
        private bool Matches(ISerializableEntity target, SerializableProperty property)
        {
            object propertyValue = property.GetValue(target);

            if (propertyValue == null)
            {
                DebugConsole.ThrowError("Couldn't compare " + AttributeValue.ToString() + " (" + AttributeValue.GetType() + ") to property \"" + property.Name + "\" - property.GetValue() returns null!");
                return false;
            }

            Type type = propertyValue.GetType();
            float? floatProperty = null;
            if (type == typeof(float) || type == typeof(int))
            {
                floatProperty = (float)propertyValue;
            }

            switch (Operator)
            {
                case OperatorType.Equals:
                    if (type == typeof(bool))
                    {
                        return ((bool)propertyValue) == (AttributeValue == "true" || AttributeValue == "True");
                    }
                    else if (FloatValue == null)
                    {
                        return propertyValue.ToString().Equals(AttributeValue);
                    }
                    else
                    {
                        return propertyValue.Equals(FloatValue);
                    }
                case OperatorType.NotEquals:
                    if (type == typeof(bool))
                    {
                        return ((bool)propertyValue) != (AttributeValue == "true" || AttributeValue == "True");
                    }
                    else if (FloatValue == null)
                    {
                        return !propertyValue.ToString().Equals(AttributeValue);
                    }
                    else
                    {
                        return !propertyValue.Equals(FloatValue);
                    }
                case OperatorType.GreaterThan:
                    if (FloatValue == null)
                    {
                        DebugConsole.ThrowError("Couldn't compare " + AttributeValue.ToString() + " (" + AttributeValue.GetType() + ") to property \"" + property.Name + "\" (" + type + ")! "
                            + "Make sure the type of the value set in the config files matches the type of the property.");
                    }
                    else if (floatProperty > FloatValue)
                    {
                        return true;
                    }
                    break;
                case OperatorType.LessThan:
                    if (FloatValue == null)
                    {
                        DebugConsole.ThrowError("Couldn't compare " + AttributeValue.ToString() + " (" + AttributeValue.GetType() + ") to property \"" + property.Name + "\" (" + type + ")! "
                            + "Make sure the type of the value set in the config files matches the type of the property.");
                    }
                    else if (floatProperty < FloatValue)
                    {
                        return true;
                    }
                    break;
                case OperatorType.GreaterThanEquals:
                    if (FloatValue == null)
                    {
                        DebugConsole.ThrowError("Couldn't compare " + AttributeValue.ToString() + " (" + AttributeValue.GetType() + ") to property \"" + property.Name + "\" (" + type + ")! "
                            + "Make sure the type of the value set in the config files matches the type of the property.");
                    }
                    else if (floatProperty >= FloatValue)
                    {
                        return true;
                    }
                    break;
                case OperatorType.LessThanEquals:
                    if (FloatValue == null)
                    {
                        DebugConsole.ThrowError("Couldn't compare " + AttributeValue.ToString() + " (" + AttributeValue.GetType() + ") to property \"" + property.Name + "\" (" + type + ")! "
                            + "Make sure the type of the value set in the config files matches the type of the property.");
                    }
                    else if (floatProperty <= FloatValue)
                    {
                        return true;
                    }
                    break;
            }
            return false;
        }
    }

}
