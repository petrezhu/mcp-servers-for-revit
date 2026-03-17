using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Interfaces;
using RevitMCPSDK.API.Models.JsonRPC;
using RevitMCPSDK.Exceptions;
using System;
using System.Collections.Generic;

namespace revit_mcp_plugin.Core
{
    public class CommandExecutor
    {
        private static readonly Dictionary<string, string> CommandAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["execute"] = "exec",
                ["exec"] = "execute"
            };

        private readonly ICommandRegistry _commandRegistry;
        private readonly ILogger _logger;

        public CommandExecutor(ICommandRegistry commandRegistry, ILogger logger)
        {
            _commandRegistry = commandRegistry;
            _logger = logger;
        }

        /// <summary>
        /// Executes a Revit command declared inside a JSON-RPC request.
        /// </summary>
        /// <param name="request">A JSON-RPC request.</param>
        /// <returns></returns>
        public string ExecuteCommand(JsonRPCRequest request)
        {
            try
            {
                var requestedMethod = request.Method;
                IRevitCommand command;
                string resolvedMethod;
                if (!TryResolveCommand(requestedMethod, out resolvedMethod, out command))
                {
                    string registeredCommands = string.Join(", ", _commandRegistry.GetRegisteredCommands());
                    _logger.Warning("未找到命令: {0}\nCommand not found: {0}\n已注册命令 / Registered commands: {1}",
                        requestedMethod, requestedMethod, string.IsNullOrWhiteSpace(registeredCommands) ? "<none>" : registeredCommands);
                    return CreateErrorResponse(request.Id,
                        JsonRPCErrorCodes.MethodNotFound,
                        $"未找到方法: '{requestedMethod}'\nMethod not found: '{requestedMethod}'");
                }

                _logger.Info("执行命令: {0}", resolvedMethod);

                // 执行命令
                // Execute command
                try
                {
                    object result = command.Execute(request.GetParamsObject(), request.Id);
                    _logger.Info("命令 {0} 执行成功\nCommand {0} executed successfully.", resolvedMethod);

                    return CreateSuccessResponse(request.Id, result);
                }
                catch (CommandExecutionException ex)
                {
                    _logger.Error("命令 {0} 执行失败: {1}\nCommand {0} failed to execute: {1}", resolvedMethod, ex.Message);
                    return CreateErrorResponse(request.Id,
                        ex.ErrorCode,
                        ex.Message,
                        ex.ErrorData);
                }
                catch (Exception ex)
                {
                    _logger.Error("命令 {0} 执行时发生异常: {1}\nAn exception occurred while executing command {0}: {1}", resolvedMethod, ex.Message);
                    return CreateErrorResponse(request.Id,
                        JsonRPCErrorCodes.InternalError,
                        ex.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("执行命令处理过程中发生异常: {0}\nAn exception has occurred durion command execution: {0}", ex.Message);
                return CreateErrorResponse(request.Id,
                    JsonRPCErrorCodes.InternalError,
                    $"内部错误: {ex.Message}\nInternal error: {ex.Message}");
            }
        }

        /// <summary>
        /// 尝试按请求名和兼容别名解析命令，兼容 exec / execute 的不同历史命名。
        /// </summary>
        /// <param name="requestedMethod">客户端请求的方法名。</param>
        /// <param name="resolvedMethod">最终解析到的命令名。</param>
        /// <param name="command">命令实例。</param>
        /// <returns>找到命令时返回 true，否则返回 false。</returns>
        private bool TryResolveCommand(string requestedMethod, out string resolvedMethod, out IRevitCommand command)
        {
            resolvedMethod = requestedMethod;
            command = null;

            if (_commandRegistry.TryGetCommand(requestedMethod, out command))
            {
                return true;
            }

            string aliasedMethod;
            if (!CommandAliases.TryGetValue(requestedMethod, out aliasedMethod))
            {
                return false;
            }

            if (!_commandRegistry.TryGetCommand(aliasedMethod, out command))
            {
                return false;
            }

            resolvedMethod = aliasedMethod;
            return true;
        }

        private string CreateSuccessResponse(string id, object result)
        {
            var response = new JsonRPCSuccessResponse
            {
                Id = id,
                Result = result is JToken jToken ? jToken : JToken.FromObject(result)
            };

            return response.ToJson();
        }

        private string CreateErrorResponse(string id, int code, string message, object data = null)
        {
            var response = new JsonRPCErrorResponse
            {
                Id = id,
                Error = new JsonRPCError
                {
                    Code = code,
                    Message = message,
                    Data = data != null ? JToken.FromObject(data) : null
                }
            };

            return response.ToJson();
        }
    }
}
