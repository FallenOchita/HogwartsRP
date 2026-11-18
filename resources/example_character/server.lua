print('example_character loaded')
RegisterCommand('hello', function(args)
    print('Hello, ' .. args)
end, false)
