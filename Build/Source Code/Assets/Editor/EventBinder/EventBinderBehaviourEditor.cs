﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace EventBinder
{
    [CustomEditor(typeof(EventBinderBehaviour))]
    public class EventBinderBehaviourEditor: Editor
    {
        private static List<Delegate> actionsList = new List<Delegate>();
        private static List<string> actionsNamesList = new List<string>();

        private bool showArguments;
        
        // Called everytime the Component is in focus 
        public override void OnInspectorGUI()
        {
            if (Application.isPlaying) return;
            
            Undo.RecordObject(target, "EventTriggerBehaviour");
            
            EventBinderBehaviour behaviour = target as EventBinderBehaviour;

            DrawDefaultInspector();
            
            EditorGUILayout.Space();
            
            // Select the EventsCollection class - let the user add his own class
            behaviour.eventsCollectionScript = EditorGUILayout.ObjectField ("Events Class", behaviour.eventsCollectionScript, typeof(MonoScript), true) as TextAsset;

            MonoScript eventsScript = null;
            try
            {
                eventsScript = (MonoScript) behaviour.eventsCollectionScript;
            }
            // If no class was selected, select the default class, with the name "EventsCollection" 
            catch (Exception e)
            {
                string[] assets = AssetDatabase.FindAssets ("EventsCollection");
                string guid = AssetDatabase.GUIDToAssetPath (assets[0]);

                eventsScript = AssetDatabase.LoadAssetAtPath<MonoScript> (guid);
                behaviour.eventsCollectionScript = eventsScript;
            }

            behaviour.eventsCollectionClassType = new SerializableSystemType(eventsScript.GetClass());
            
            // Add a GUI Space
            EditorGUILayout.Space();
            
            
            // Check if behaviour is null & Setup Target GameObject
            if (behaviour != null && behaviour.targetObject == null) 
                behaviour.targetObject = behaviour.gameObject;
            
            // Set the EventTypeIndex , Between: "From Target Object" & "EventTrigger"
            // 1. From Target Object - gets an UI Event from every component of the object (eg: onClick event from Button)
            // 2. EventTrigger - displays and listens to any event from the EventTriggerType class
            behaviour.eventTypeIndex = EditorGUILayout.Popup ("Event Type", behaviour.eventTypeIndex, new []{"From Target Object - UnityEvent", "EventTrigger Type"});
                
            bool eventsFound = true;

            // If "From Target Object" is selected
            if (behaviour.eventTypeIndex == 0)
            {
                behaviour.ClearEventTrigger();
                
                List<string> componentNames = new List<string>();
                List<Component> componentsList = new List<Component>();
                
                List<string> propertiesNames = new List<string>();
                
                foreach (Component loopComponent in behaviour.targetObject.GetComponents(typeof(Component)))
                {
                    componentsList.Add (loopComponent);
                    componentNames.Add (loopComponent.GetType().ToString());
                }
                
                behaviour.eventComponentIndex = EditorGUILayout.Popup ("Component", behaviour.eventComponentIndex, componentNames.ToArray());

                if (behaviour.eventComponentIndex != -1 && behaviour.eventComponentIndex < componentsList.Count)
                {
                    Component eventComponent = componentsList.ElementAt (behaviour.eventComponentIndex);

                    // Iterate through all Properties of the selected component
                    foreach (PropertyInfo propertyInfo in eventComponent.GetType().GetProperties (BindingFlags.Instance | BindingFlags.Public))
                    {
                        // If the property is an UnityEvent -> Add the name to the "propertiesNames" list
                        if (propertyInfo.PropertyType.BaseType == typeof(UnityEvent)) propertiesNames.Add (propertyInfo.Name);
                    }

                    // If the "propertiesNames" list is Empty -> tell the user that no events have been found
                    if (propertiesNames.Count == 0)
                    {
                        eventsFound = false;
                        EditorGUILayout.LabelField ("No Events found");
                    }
                    // If at least one event has been found
                    else
                    {
                        // Display a Popup Dropdown with the events found for the user to select    
                        behaviour.eventPropertyIndex = EditorGUILayout.Popup ("Event Property", behaviour.eventPropertyIndex, propertiesNames.ToArray());

                        if (behaviour.eventPropertyIndex != -1 && propertiesNames.Count > behaviour.eventPropertyIndex)
                        {
                            behaviour.selectedUnityEventBase = eventComponent.GetType().GetProperty (propertiesNames.ElementAt (behaviour.eventPropertyIndex)).GetValue (eventComponent, null) as UnityEventBase;
                            behaviour.RefreshUnityEventBase();
                        }
                    }
                }
            }
            // If an event from the "EventTriggerType" class is selected
            else if (behaviour.eventTypeIndex == 1)
            {
                behaviour.eventTriggerType = (EventTriggerType) behaviour.eventIndex;    

                if (!Application.isPlaying) behaviour.RefreshEventTriggerList();
                
                behaviour.eventIndex = EditorGUILayout.Popup ("Event Trigger", behaviour.eventIndex, Enum.GetNames (typeof(EventTriggerType)));
            }

            // If no event is found, Save GUI and Stop
            if (!eventsFound)
            {
                if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                return;
            }
            
            // Get actions list
            // Save the Actions in "actionsList"
            // Save the Action names in the "actionNamesList"
            
            actionsList = new List<Delegate>();
            actionsNamesList = new List<string>();
            FieldInfo[] fieldsCollection = behaviour.eventsCollectionClassType.SystemType.GetFields (BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            foreach (FieldInfo fieldInfo in fieldsCollection)
            {
                actionsList.Add (fieldInfo.GetValue (null) as Delegate); 
                actionsNamesList.Add (fieldInfo.Name);
            }
            
            // Adds 2 vertical spaces to the GUI
            EditorGUILayout.Space(); EditorGUILayout.Space();
            
            // Save the deleateIndex and the targetDelegate to the Behaviour class after the user has made a choice
            if (behaviour.delegateIndex > actionsNamesList.Count) behaviour.delegateIndex = 0;
            behaviour.delegateIndex = EditorGUILayout.Popup ("Event Delegate", behaviour.delegateIndex, actionsNamesList.ToArray());
            behaviour.targetDelegate = actionsList[behaviour.delegateIndex];
 
            // Initializez all List components from the behaviour
            CreateArgumentsLists (behaviour);
            
            // If TargetDelegate from Behaviour is not null
            if(behaviour.targetDelegate != null)
            {
                ParameterInfo[] parametersList = behaviour.targetDelegate.Method.GetParameters();
                
                // If the selected TargetDelegate has any parameters -> Add 2 spaces to the GUI
                if (parametersList.Length > 0)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.Space();

                    showArguments = EditorGUILayout.Foldout (showArguments,"Arguments");

                    if (showArguments)
                    {
                        // Iterate through all parameters
                        for (int index = 0; index < parametersList.Length; index++)
                        {
                            ParameterInfo parameterInfo = parametersList[index];

                            string textFieldName = parameterInfo.Name;
                            if (textFieldName == "") textFieldName = "Argument " + (index + 1);

                            // Show the NAME and the TYPE of the parameter
                            EditorGUILayout.LabelField (textFieldName + ": " + parameterInfo.ParameterType, EditorStyles.boldLabel);

                            behaviour.argsChoiceIndexList[index] = EditorGUILayout.Popup ("Argument type", behaviour.argsChoiceIndexList[index], Enum.GetNames (typeof(EventArgumentKind)));
                            //STRING
                            if (parameterInfo.ParameterType == typeof(string))
                            {
                                if (behaviour.argsChoiceIndexList[index] == 0) behaviour.stringArgs[index] = EditorGUILayout.TextField ("Value", behaviour.stringArgs[index]);
                                else SetupDynamicArgument (behaviour, index, typeof(string));

                                behaviour.argumentTypes[index] = EventArgumentType.String;
                            }

                            /**SYSTEM TYPE ARGUMENTS*/

                            //BOOLEAN
                            if (parameterInfo.ParameterType == typeof(bool))
                            {
                                if (behaviour.argsChoiceIndexList[index] == 0)
                                {
                                    if (string.IsNullOrEmpty (behaviour.stringArgs[index])) behaviour.stringArgs[index] = "false";
                                    behaviour.stringArgs[index] = EditorGUILayout.Toggle ("Value", Convert.ToBoolean (behaviour.stringArgs[index])).ToString();
                                }
                                else SetupDynamicArgument (behaviour, index, typeof(bool));

                                behaviour.argumentTypes[index] = EventArgumentType.Boolean;
                            }

                            /**NUMBER TYPE ARGUMENTS*/

                            //INT
                            else if (parameterInfo.ParameterType == typeof(int))
                            {
                                if (behaviour.argsChoiceIndexList[index] == 0)
                                {
                                    if (string.IsNullOrEmpty (behaviour.stringArgs[index])) behaviour.stringArgs[index] = "0";
                                    behaviour.stringArgs[index] = EditorGUILayout.IntField ("Value", behaviour.stringArgs[index].ParseToInt()).ToString();
                                }
                                else SetupDynamicArgument (behaviour, index, typeof(int));

                                behaviour.argumentTypes[index] = EventArgumentType.Int;
                            }
                            //FLOAT
                            else if (parameterInfo.ParameterType == typeof(float))
                            {
                                if (behaviour.argsChoiceIndexList[index] == 0)
                                {
                                    if (string.IsNullOrEmpty (behaviour.stringArgs[index])) behaviour.stringArgs[index] = "0";
                                    behaviour.stringArgs[index] = EditorGUILayout.FloatField ("Value", behaviour.stringArgs[index].ParseToFloat()).ToString (CultureInfo.InvariantCulture);
                                }
                                else SetupDynamicArgument (behaviour, index, typeof(float));

                                behaviour.argumentTypes[index] = EventArgumentType.Float;
                            }
                            //DOUBLE
                            else if (parameterInfo.ParameterType == typeof(double))
                            {
                                if (behaviour.argsChoiceIndexList[index] == 0)
                                {
                                    if (string.IsNullOrEmpty (behaviour.stringArgs[index])) behaviour.stringArgs[index] = "0";
                                    behaviour.stringArgs[index] = EditorGUILayout.DoubleField ("Value", behaviour.stringArgs[index].ParseToDouble()).ToString (CultureInfo.InvariantCulture);
                                }
                                else SetupDynamicArgument (behaviour, index, typeof(double));

                                behaviour.argumentTypes[index] = EventArgumentType.Double;
                            }

                            /**VECTOR TYPE ARGUMENTS*/

                            //VECTOR 2
                            else if (parameterInfo.ParameterType == typeof(Vector2))
                            {
                                if (behaviour.argsChoiceIndexList[index] == 0)
                                {
                                    if (string.IsNullOrEmpty (behaviour.stringArgs[index]) || !behaviour.stringArgs[index].CanDeserializeToVector2())
                                        behaviour.stringArgs[index] = new Vector2().SerializeToString();
                                    behaviour.stringArgs[index] = EditorGUILayout.Vector2Field ("Value", behaviour.stringArgs[index].DeserializeToVector2()).SerializeToString();
                                }
                                else SetupDynamicArgument (behaviour, index, typeof(Vector2));

                                behaviour.argumentTypes[index] = EventArgumentType.Vector2;
                            }
                            //VECTOR 3
                            else if (parameterInfo.ParameterType == typeof(Vector3))
                            {
                                if (behaviour.argsChoiceIndexList[index] == 0)
                                {
                                    if (string.IsNullOrEmpty (behaviour.stringArgs[index]) || !behaviour.stringArgs[index].CanDeserializeToVector3())
                                        behaviour.stringArgs[index] = new Vector3().SerializeToString();
                                    behaviour.stringArgs[index] = EditorGUILayout.Vector3Field ("Value", behaviour.stringArgs[index].DeserializeToVector3()).SerializeToString();
                                }
                                else SetupDynamicArgument (behaviour, index, typeof(Vector3));

                                behaviour.argumentTypes[index] = EventArgumentType.Vector3;
                            }
                            //VECTOR 4
                            else if (parameterInfo.ParameterType == typeof(Vector4))
                            {
                                if (behaviour.argsChoiceIndexList[index] == 0)
                                {
                                    if (string.IsNullOrEmpty (behaviour.stringArgs[index]) || !behaviour.stringArgs[index].CanDeserializeToVector4())
                                        behaviour.stringArgs[index] = new Vector4().SerializeToString();
                                    behaviour.stringArgs[index] = EditorGUILayout.Vector4Field ("Value", behaviour.stringArgs[index].DeserializeToVector4()).SerializeToString();
                                }
                                else SetupDynamicArgument (behaviour, index, typeof(Vector4));

                                behaviour.argumentTypes[index] = EventArgumentType.Vector4;
                            }

                            /**GAME OBJECT TYPE ARGUMENTS*/

                            //GAME OBJECT
                            else if (parameterInfo.ParameterType == typeof(GameObject))
                            {
                                if (behaviour.argsChoiceIndexList[index] == 0)
                                    behaviour.gameObjectArgs[index] = EditorGUILayout.ObjectField ("Value", behaviour.gameObjectArgs[index], typeof(GameObject), true) as GameObject;
                                else SetupDynamicArgument (behaviour, index, typeof(GameObject));

                                behaviour.argumentTypes[index] = EventArgumentType.GameObject;
                            }

                            //COMPONENT
                            else if (parameterInfo.ParameterType == typeof(Component))
                            {
                                if (behaviour.argsChoiceIndexList[index] == 0)
                                    behaviour.componentArgs[index] = EditorGUILayout.ObjectField ("Value", behaviour.componentArgs[index], typeof(Component), true) as Component;
                                else SetupDynamicArgument (behaviour, index, typeof(Component));

                                behaviour.argumentTypes[index] = EventArgumentType.Component;
                            }

                            /**OTHER TYPE ARGUMENTS*/

                            //COLOR
                            else if (parameterInfo.ParameterType == typeof(Color))
                            {
                                if (behaviour.argsChoiceIndexList[index] == 0) behaviour.colorArgs[index] = EditorGUILayout.ColorField ("Value", behaviour.colorArgs[index]);
                                else SetupDynamicArgument (behaviour, index, typeof(Color));

                                behaviour.argumentTypes[index] = EventArgumentType.Color;
                            }

                            //ENUM
                            else if (parameterInfo.ParameterType.BaseType == typeof(Enum))
                            {
                                if (behaviour.argsChoiceIndexList[index] == 0)
                                {
                                    string[] enumNames = Enum.GetNames (parameterInfo.ParameterType);
                                    int indexPrevSelectedArg = Array.IndexOf (enumNames, behaviour.stringArgs[index]);
                                    if (indexPrevSelectedArg == -1) indexPrevSelectedArg = 0;
                                    behaviour.stringArgs[index] = enumNames.GetValue (EditorGUILayout.Popup ("Value", indexPrevSelectedArg, enumNames)).ToString();
                                }
                                else SetupDynamicArgument (behaviour, index, parameterInfo.ParameterType);

                                behaviour.argumentTypes[index] = EventArgumentType.Enum;
                            }

                            if (index < parametersList.Length - 1)
                            {
                                EditorGUILayout.Space();
                                EditorGUILayout.Space();
                            }
                        }
                    }
                }

            }

            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private void SetupDynamicArgument(EventBinderBehaviour behaviour, int index, Type argumentType)
        {
            behaviour.argsGameObjectTarget = EditorGUILayout.ObjectField ("Game Object", behaviour.argsGameObjectTarget, typeof(GameObject), true) as GameObject;

            if (behaviour.argsGameObjectTarget == null) return;
            
            List<string> propertiesNames = new List<string>();
            List<string> componentNames = new List<string>();
            List<Component> componentsList = new List<Component>();
                            
                            
            foreach (Component loopComponent in behaviour.argsGameObjectTarget.GetComponents(typeof(Component)))
            {
                componentsList.Add (loopComponent);
                componentNames.Add (loopComponent.GetType().ToString());
            }
                            
            behaviour.argsComponentIndexes[index] = EditorGUILayout.Popup ("Component", behaviour.argsComponentIndexes[index], componentNames.ToArray());
            behaviour.argsTargetsProperties[index] = null;
            
            if (behaviour.argsComponentIndexes[index] != -1 && componentsList.Count > behaviour.argsComponentIndexes[index])
            {
                behaviour.argsComponents[index] = componentsList.ElementAt (behaviour.argsComponentIndexes[index]);
                    
                foreach (PropertyInfo propertyInfo in behaviour.argsComponents[index].GetType().GetProperties (BindingFlags.Instance | BindingFlags.Public))
                    if(propertyInfo.PropertyType == argumentType) propertiesNames.Add (propertyInfo.Name);

                if (propertiesNames.Count == 0)
                {
                    EditorGUILayout.LabelField ("No properties of type [" +  argumentType + "] found", EditorStyles.label);
                }
                else
                {
                    behaviour.argsTargetsPropertiesIndexes[index] = EditorGUILayout.Popup ("Property", behaviour.argsTargetsPropertiesIndexes[index], propertiesNames.ToArray());

                    if (behaviour.argsTargetsPropertiesIndexes[index] != -1 && propertiesNames.Count > behaviour.argsTargetsPropertiesIndexes[index])
                        behaviour.argsTargetsProperties[index] = propertiesNames.ElementAt (behaviour.argsTargetsPropertiesIndexes[index]);
                }
            }
        }
        

        private void CreateArgumentsLists(EventBinderBehaviour behaviour)
        {
            if (behaviour.targetDelegate == null)
                return;
            
            int length = behaviour.targetDelegate.Method.GetParameters().Length;
                
            
            //todo: Add || ( OR ) for every if - combining them;
            
            
            if(behaviour.argumentTypes == null || behaviour.argumentTypes.Length != length) behaviour.argumentTypes = new EventArgumentType[length];
//            if() behaviour.argumentTypes = new EventArgumentType[length];
            
            if(behaviour.argsChoiceIndexList == null)          behaviour.argsChoiceIndexList = new int[length];
            if(behaviour.argsChoiceIndexList.Length != length) behaviour.argsChoiceIndexList = new int[length];
            
            
            if(behaviour.argsComponents == null)          behaviour.argsComponents = new Component[length];
            if(behaviour.argsComponents.Length != length) behaviour.argsComponents = new Component[length];
            
            
            if(behaviour.argsTargetsProperties == null)          behaviour.argsTargetsProperties = new string[length];
            if(behaviour.argsTargetsProperties.Length != length) behaviour.argsTargetsProperties = new string[length];
            
            if(behaviour.argsComponentIndexes == null)          behaviour.argsComponentIndexes = new int[length];
            if(behaviour.argsComponentIndexes.Length != length) behaviour.argsComponentIndexes = new int[length];
            
            if(behaviour.argsTargetsPropertiesIndexes == null)          behaviour.argsTargetsPropertiesIndexes = new int[length];
            if(behaviour.argsTargetsPropertiesIndexes.Length != length) behaviour.argsTargetsPropertiesIndexes = new int[length];
            
            
            /**TYPE OF ARGUMENTS*/
            if(behaviour.stringArgs == null)          behaviour.stringArgs = new string[length];
            if(behaviour.stringArgs.Length != length) behaviour.stringArgs = new string[length];
             
            if(behaviour.gameObjectArgs == null)          behaviour.gameObjectArgs = new GameObject[length];
            if(behaviour.gameObjectArgs.Length != length) behaviour.gameObjectArgs = new GameObject[length];
            
            if(behaviour.componentArgs == null)          behaviour.componentArgs = new Component[length];
            if(behaviour.componentArgs.Length != length) behaviour.componentArgs = new Component[length];
            
            if(behaviour.colorArgs == null)          behaviour.colorArgs = new Color[length];
            if(behaviour.colorArgs.Length != length) behaviour.colorArgs = new Color[length];
        }
        
    }
}