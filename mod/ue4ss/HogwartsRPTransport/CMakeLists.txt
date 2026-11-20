cmake_minimum_required(VERSION 3.22)
project(hogwartsrp_transport LANGUAGES CXX)

if(NOT WIN32)
  message(FATAL_ERROR "HogwartsRP UE4SS transport currently targets Windows only.")
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

find_path(LUA_INCLUDE_DIR lua.h HINTS "$ENV{UE4SS_LUA_INCLUDE_DIR}" REQUIRED)
find_library(LUA_LIBRARY NAMES lua54 lua5.4 lua HINTS "$ENV{UE4SS_LUA_LIBRARY_DIR}" REQUIRED)

add_library(hogwartsrp_transport SHARED src/transport.cpp)
target_include_directories(hogwartsrp_transport PRIVATE "${LUA_INCLUDE_DIR}")
target_link_libraries(hogwartsrp_transport PRIVATE "${LUA_LIBRARY}" winhttp)
target_compile_definitions(hogwartsrp_transport PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX)
set_target_properties(hogwartsrp_transport PROPERTIES PREFIX "" OUTPUT_NAME "hogwartsrp_transport")
